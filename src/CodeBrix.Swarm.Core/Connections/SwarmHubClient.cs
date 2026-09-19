using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core.Diagnostics;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// The connection a Hive or a Worker keeps to the coordinator, and the rule about how long the
/// coordinator may stay out of reach. Both roles need exactly this, so it is written once: connect
/// carrying a token, retry for as long as the window allows, register handlers by message kind,
/// reconnect by the same rule after a drop, and say so when the window finally runs out.
/// </summary>
/// <remarks>
/// <para>
/// The retry schedule starts quickly and settles down - the first wait is short and each one after
/// it is twice the last, up to a few seconds - and the whole sequence is bounded by the window. When
/// the window runs out, <see cref="QueenUnreachable" /> is raised once and no further attempt is
/// made until something starts the client again.
/// </para>
/// <para>
/// Every wait and every reading of the time goes through the clock this was constructed with, so a
/// test can exercise a minute-long rule instantly.
/// </para>
/// </remarks>
internal sealed class SwarmHubClient : IAsyncDisposable
{
    private readonly SwarmRole _role;
    private readonly SwarmConnectionOptions _options;
    private readonly ISwarmHubConnectionFactory _factory;
    private readonly ISwarmClock _clock;
    private readonly SwarmMessageDispatcher _dispatcher = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _hubUrl;

    private ISwarmHubConnection _connection;
    private volatile bool _isConnected;
    private volatile bool _isStopping;
    private volatile bool _isDisposed;

    /// <summary>
    /// Creates a client for one role, using the real SignalR connection and the machine's clock.
    /// </summary>
    /// <param name="role">Which hub to connect to and which built-in terminate kind to answer.</param>
    /// <param name="options">Where the coordinator is, the token, and the retry window.</param>
    public SwarmHubClient(SwarmRole role, SwarmConnectionOptions options)
        : this(role, options, SignalRHubConnectionFactory.Instance, SystemSwarmClock.Instance) { }

    /// <summary>
    /// Creates a client with a substituted connection factory and clock, for tests.
    /// </summary>
    /// <param name="role">Which hub to connect to and which built-in terminate kind to answer.</param>
    /// <param name="options">Where the coordinator is, the token, and the retry window.</param>
    /// <param name="connectionFactory">What makes each connection attempt's connection.</param>
    /// <param name="clock">What reads the time and does the waiting.</param>
    public SwarmHubClient(
        SwarmRole role,
        SwarmConnectionOptions options,
        ISwarmHubConnectionFactory connectionFactory,
        ISwarmClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(clock);

        options.Validate();

        _role = role;
        _options = options;
        _factory = connectionFactory;
        _clock = clock;
        _hubUrl = SwarmHubContract.HubUrlFor(options.QueenUrl, role);
    }

    /// <summary>The role this client connects as.</summary>
    public SwarmRole Role => _role;

    /// <summary>The address of the hub this client connects to.</summary>
    public string HubUrl => _hubUrl;

    /// <summary>The handlers registered for the kinds of message this process cares about.</summary>
    public SwarmMessageDispatcher Messages => _dispatcher;

    /// <summary>True while the connection is open.</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Raised each time the connection opens, including after a reconnect.</summary>
    public event EventHandler Connected;

    /// <summary>Raised each time an open connection goes away.</summary>
    public event EventHandler Disconnected;

    /// <summary>
    /// Raised when the retry window has run out with no connection. This is the end of the road: the
    /// Hive or Worker that owns this client winds itself down.
    /// </summary>
    public event EventHandler QueenUnreachable;

    /// <summary>
    /// Raised for anything the client dealt with by itself - a failed attempt, a dropped connection,
    /// a handler that threw.
    /// </summary>
    public event EventHandler<SwarmConnectionErrorEventArgs> Error;

    /// <summary>
    /// Registers what to do when the coordinator sends this role's built-in terminate message.
    /// </summary>
    /// <param name="handler">What to do. It is given the process's shutdown token.</param>
    public void OnTerminate(Func<CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _dispatcher.Register(
            SwarmMessageKinds.TerminateKindFor(_role),
            (message, cancellationToken) => handler(cancellationToken));
    }

    /// <summary>
    /// Opens the connection, retrying until it succeeds or the window runs out.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to give up before the window does.</param>
    /// <returns>
    /// True when the connection is open. False when the window ran out first, in which case
    /// <see cref="QueenUnreachable" /> has been raised.
    /// </returns>
    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        await _gate.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            if (_connection != null)
            {
                return true;
            }

            return await ConnectWithinWindowAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Closes the connection in an orderly way and stops reconnecting.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to stop waiting for the close.</param>
    /// <returns>A task that completes once the connection is closed.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed)
        {
            return;
        }

        _isStopping = true;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var connection = _connection;
            _connection = null;
            _isConnected = false;

            if (connection == null)
            {
                return;
            }

            try
            {
                await connection.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseError("stop", ex);
            }

            await SafeDisposeAsync(connection).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isStopping = true;

        try
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseError("dispose", ex);
        }

        var connection = _connection;
        _connection = null;
        _isConnected = false;

        if (connection != null)
        {
            await SafeDisposeAsync(connection).ConfigureAwait(false);
        }

        _lifetime.Dispose();
        _gate.Dispose();
    }

    private async Task<bool> ConnectWithinWindowAsync(CancellationToken cancellationToken)
    {
        var deadline = _clock.UtcNow + _options.QueenUnreachableWindow;
        var delay = _options.FirstRetryDelay;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ISwarmHubConnection attempt = null;

            try
            {
                attempt = _factory.Create(_hubUrl, _options.Token);
                attempt.OnMessage(SwarmHubContract.ReceiveMessageMethodName, OnMessageReceivedAsync);

                //The handler knows which connection it belongs to, so that a close raised while an
                //attempt that never became the live connection is being thrown away cannot be
                //mistaken for the live connection dropping.
                var closing = attempt;
                attempt.Closed += error => OnConnectionClosedAsync(closing, error);

                //Set before the attempt is made, so a close that arrives the instant the connection
                //opens is recognised; cleared again below if the attempt fails.
                _connection = attempt;

                await attempt.StartAsync(cancellationToken).ConfigureAwait(false);

                _isConnected = true;
                Connected?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (OperationCanceledException)
            {
                _connection = null;
                await SafeDisposeAsync(attempt).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                _connection = null;
                await SafeDisposeAsync(attempt).ConfigureAwait(false);
                RaiseError("connect", ex);
            }

            if (_clock.UtcNow >= deadline)
            {
                QueenUnreachable?.Invoke(this, EventArgs.Empty);
                return false;
            }

            var remaining = deadline - _clock.UtcNow;
            var wait = delay < remaining ? delay : remaining;

            if (wait > TimeSpan.Zero)
            {
                await _clock.DelayAsync(wait, cancellationToken).ConfigureAwait(false);
            }

            var doubled = TimeSpan.FromTicks(delay.Ticks * 2);
            delay = doubled > _options.MaximumRetryDelay ? _options.MaximumRetryDelay : doubled;
        }
    }

    private async Task OnConnectionClosedAsync(ISwarmHubConnection source, Exception error)
    {
        if (!ReferenceEquals(source, _connection))
        {
            //An attempt that never became the live connection, or one this client has already let
            //go of. Nothing to report and nothing to reconnect.
            return;
        }

        _connection = null;
        _isConnected = false;

        await SafeDisposeAsync(source).ConfigureAwait(false);

        Disconnected?.Invoke(this, EventArgs.Empty);

        if (_isStopping || _isDisposed)
        {
            return;
        }

        RaiseError("connection closed", error);

        //The reconnect runs on its own: this handler is called from inside the connection that just
        //went away, and waiting for a new one here would hold that connection's own shutdown open.
        _ = Task.Run(ReconnectAsync);
    }

    private async Task ReconnectAsync()
    {
        try
        {
            await _gate.WaitAsync(_lifetime.Token).ConfigureAwait(false);

            try
            {
                if (_isStopping || _isDisposed || _connection != null)
                {
                    return;
                }

                await ConnectWithinWindowAsync(_lifetime.Token).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            //The client is being stopped or disposed; there is nothing to reconnect to.
        }
        catch (ObjectDisposedException)
        {
            //The client was disposed while this was waiting for its turn.
        }
        catch (Exception ex)
        {
            RaiseError("reconnect", ex);
        }
    }

    private async Task OnMessageReceivedAsync(SwarmMessage message)
    {
        if (message == null)
        {
            return;
        }

        try
        {
            await _dispatcher.DispatchAsync(message, _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            //Shutting down while a handler was running.
        }
        catch (Exception ex)
        {
            RaiseError($"handling '{message.Kind}'", ex);
        }
    }

    private async Task SafeDisposeAsync(ISwarmHubConnection connection)
    {
        if (connection == null)
        {
            return;
        }

        try
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseError("disposing a connection", ex);
        }
    }

    private void RaiseError(string context, Exception error)
        => Error?.Invoke(this, new SwarmConnectionErrorEventArgs(context, error));

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SwarmHubClient));
        }
    }
}
