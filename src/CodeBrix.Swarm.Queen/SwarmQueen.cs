using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Messaging;
using CodeBrix.Swarm.Queen.Authentication;
using CodeBrix.Swarm.Queen.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Swarm.Queen;

/// <summary>
/// The coordinator of a swarm: a running web host with two authenticated hubs on it, something to
/// send messages with, the counts of what is connected, and the means to mint the access tokens
/// Hives and Workers need.
/// </summary>
/// <remarks>
/// <para>
/// THE LIBRARY OWNS THE WEB HOST. A consuming application does not have to be a web application -
/// it is usually a desktop one - so it hands over an address and a secret and gets back this object.
/// The two hubs are mapped explicitly, by name, rather than found by looking through the assembly.
/// </para>
/// <para>
/// TRAFFIC IS ONE-WAY AND NOTHING IS KEPT. The hubs expose no method a client can call, and a
/// message reaches whatever is connected at the moment it is sent - nothing is replayed to something
/// that connects later.
/// </para>
/// </remarks>
public sealed class SwarmQueen : IAsyncDisposable
{
    private readonly WebApplication _application;
    private readonly SwarmTokenAuthority _authority;
    private readonly SwarmConnectionRegistry _registry;
    private readonly IHubContext<SwarmHiveHub> _hiveHub;
    private readonly IHubContext<SwarmWorkerHub> _workerHub;

    private bool _isStopped;
    private bool _isDisposed;

    private SwarmQueen(
        WebApplication application,
        SwarmTokenAuthority authority,
        SwarmConnectionRegistry registry,
        string baseUrl)
    {
        _application = application;
        _authority = authority;
        _registry = registry;
        _hiveHub = application.Services.GetRequiredService<IHubContext<SwarmHiveHub>>();
        _workerHub = application.Services.GetRequiredService<IHubContext<SwarmWorkerHub>>();
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// Starts a coordinator and waits until it is listening.
    /// </summary>
    /// <param name="options">Where to listen, and the swarm's master secret.</param>
    /// <param name="cancellationToken">Cancelled to give up on starting.</param>
    /// <returns>The running coordinator.</returns>
    /// <exception cref="ArgumentNullException">The options are null.</exception>
    /// <exception cref="SwarmConfigurationException">The address is missing or malformed.</exception>
    /// <exception cref="SwarmTokenException">The master secret is missing or too short.</exception>
    public static async Task<SwarmQueen> StartAsync(SwarmQueenOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();

        var authority = new SwarmTokenAuthority(options.MasterSecret);
        var registry = new SwarmConnectionRegistry();

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(SwarmQueen).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory
        });

        //The host belongs to this library, not to the application that owns it, so it says nothing
        //unless the application has asked for its logging.
        builder.Logging.ClearProviders();

        if (options.LoggerFactory != null)
        {
            builder.Services.AddSingleton(options.LoggerFactory);
        }

        builder.Services.AddSingleton(authority);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSignalR();

        builder.Services
            .AddAuthentication(SwarmAuthorization.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SwarmTokenAuthenticationHandler>(
                SwarmAuthorization.SchemeName, null);

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(SwarmAuthorization.HivePolicy, policy =>
            {
                policy.AuthenticationSchemes.Add(SwarmAuthorization.SchemeName);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(SwarmAuthorization.RoleClaimType, SwarmRoleNames.Hive);
            })
            .AddPolicy(SwarmAuthorization.WorkerPolicy, policy =>
            {
                policy.AuthenticationSchemes.Add(SwarmAuthorization.SchemeName);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(SwarmAuthorization.RoleClaimType, SwarmRoleNames.Worker);
            });

        var application = builder.Build();

        application.Urls.Clear();
        application.Urls.Add(options.Url.Trim());

        application.UseWebSockets();
        application.UseAuthentication();
        application.UseAuthorization();

        //Mapped by name. Nothing here looks through the assembly for hub types: a swarm has exactly
        //these two hubs, and a third one appearing because somebody added a class would be a
        //surprise nobody asked for.
        application.MapHub<SwarmHiveHub>(SwarmHubContract.HiveHubPath);
        application.MapHub<SwarmWorkerHub>(SwarmHubContract.WorkerHubPath);

        await application.StartAsync(cancellationToken).ConfigureAwait(false);

        return new SwarmQueen(application, authority, registry, ReadBoundUrl(application, options.Url));
    }

    /// <summary>
    /// The address the coordinator is really listening on. When the options asked for port zero,
    /// this is where the port that was actually taken can be read.
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>The address Hives connect to.</summary>
    public string HiveHubUrl => SwarmHubContract.HubUrlFor(BaseUrl, SwarmRole.Hive);

    /// <summary>The address Workers connect to.</summary>
    public string WorkerHubUrl => SwarmHubContract.HubUrlFor(BaseUrl, SwarmRole.Worker);

    /// <summary>How many Hives are connected right now.</summary>
    public int HiveCount => _registry.HiveCount;

    /// <summary>How many Workers are connected right now.</summary>
    public int WorkerCount => _registry.WorkerCount;

    /// <summary>Raised when a Hive or a Worker arrives at its hub.</summary>
    public event EventHandler<SwarmConnectionEventArgs> Connected
    {
        add => _registry.Connected += value;
        remove => _registry.Connected -= value;
    }

    /// <summary>Raised when a Hive or a Worker leaves its hub.</summary>
    public event EventHandler<SwarmConnectionEventArgs> Disconnected
    {
        add => _registry.Disconnected += value;
        remove => _registry.Disconnected -= value;
    }

    /// <summary>
    /// Mints an access token that lets one Hive into the Hive hub, and that does not expire.
    /// </summary>
    /// <returns>The token, to be handed to a Hive.</returns>
    public string CreateHiveToken() => _authority.CreateToken(SwarmRole.Hive);

    /// <summary>
    /// Mints an access token that lets one Hive into the Hive hub for a limited time.
    /// </summary>
    /// <param name="lifetime">How long the token remains good for.</param>
    /// <returns>The token, to be handed to a Hive.</returns>
    public string CreateHiveToken(TimeSpan lifetime) => _authority.CreateToken(SwarmRole.Hive, lifetime);

    /// <summary>
    /// Mints an access token that lets one Worker into the Worker hub, and that does not expire.
    /// </summary>
    /// <returns>The token, to be put in a Worker's configuration.</returns>
    public string CreateWorkerToken() => _authority.CreateToken(SwarmRole.Worker);

    /// <summary>
    /// Mints an access token that lets one Worker into the Worker hub for a limited time.
    /// </summary>
    /// <param name="lifetime">How long the token remains good for.</param>
    /// <returns>The token, to be put in a Worker's configuration.</returns>
    public string CreateWorkerToken(TimeSpan lifetime) => _authority.CreateToken(SwarmRole.Worker, lifetime);

    /// <summary>
    /// Sends an application-defined message to every connected Hive.
    /// </summary>
    /// <param name="message">The message. Its kind must not be one the swarm reserves.</param>
    /// <param name="cancellationToken">Cancelled to give up on the send.</param>
    /// <returns>A task that completes when the message has been handed to the hub.</returns>
    /// <exception cref="ArgumentNullException">The message is null.</exception>
    /// <exception cref="ArgumentException">The message's kind is missing or is reserved.</exception>
    /// <exception cref="ObjectDisposedException">The coordinator has been stopped.</exception>
    public Task SendToHivesAsync(SwarmMessage message, CancellationToken cancellationToken)
        => SendAsync(_hiveHub.Clients, message, cancellationToken);

    /// <summary>
    /// Sends an application-defined message to every connected Worker.
    /// </summary>
    /// <param name="message">The message. Its kind must not be one the swarm reserves.</param>
    /// <param name="cancellationToken">Cancelled to give up on the send.</param>
    /// <returns>A task that completes when the message has been handed to the hub.</returns>
    /// <exception cref="ArgumentNullException">The message is null.</exception>
    /// <exception cref="ArgumentException">The message's kind is missing or is reserved.</exception>
    /// <exception cref="ObjectDisposedException">The coordinator has been stopped.</exception>
    public Task SendToWorkersAsync(SwarmMessage message, CancellationToken cancellationToken)
        => SendAsync(_workerHub.Clients, message, cancellationToken);

    /// <summary>
    /// Tells every connected Hive that the work is over: each of them ends the Workers it has,
    /// stops starting new ones for good, and reports to its own application that it has finished.
    /// Nothing undoes this.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to give up on the send.</param>
    /// <returns>A task that completes when the message has been handed to the hub.</returns>
    /// <exception cref="ObjectDisposedException">The coordinator has been stopped.</exception>
    public Task SendTerminateAllWorkersToHivesAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnusable();

        return _hiveHub.Clients.All.SendAsync(
            SwarmHubContract.ReceiveMessageMethodName,
            SwarmMessage.Create(SwarmMessageKinds.TerminateAllWorkers),
            cancellationToken);
    }

    /// <summary>
    /// Tells every connected Worker to end itself: stop the work, run the application's shutdown
    /// step, and exit normally. A Hive whose Workers end this way is free to start more.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to give up on the send.</param>
    /// <returns>A task that completes when the message has been handed to the hub.</returns>
    /// <exception cref="ObjectDisposedException">The coordinator has been stopped.</exception>
    public Task SendTerminateToWorkersAsync(CancellationToken cancellationToken)
    {
        ThrowIfUnusable();

        return _workerHub.Clients.All.SendAsync(
            SwarmHubContract.ReceiveMessageMethodName,
            SwarmMessage.Create(SwarmMessageKinds.TerminateWorker),
            cancellationToken);
    }

    /// <summary>
    /// Stops listening and closes every connection. Sending afterwards is refused.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to stop waiting for the shutdown.</param>
    /// <returns>A task that completes once the host has stopped.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        await _application.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            //Disposal never throws on the way out; the host is being thrown away regardless.
        }

        await _application.DisposeAsync().ConfigureAwait(false);
    }

    private Task SendAsync(IHubClients clients, SwarmMessage message, CancellationToken cancellationToken)
    {
        ThrowIfUnusable();
        ArgumentNullException.ThrowIfNull(message);

        if (string.IsNullOrWhiteSpace(message.Kind))
        {
            throw new ArgumentException("The message has no kind, so nothing could handle it.", nameof(message));
        }

        if (SwarmMessageKinds.IsBuiltIn(message.Kind))
        {
            throw new ArgumentException(
                $"'{message.Kind}' is one of the swarm's own message kinds. Send it with the method "
                + "named after it rather than by hand, and name application messages something that "
                + $"does not start with '{SwarmMessageKinds.ReservedPrefix}'.",
                nameof(message));
        }

        return clients.All.SendAsync(
            SwarmHubContract.ReceiveMessageMethodName, message, cancellationToken);
    }

    private void ThrowIfUnusable()
    {
        if (_isDisposed || _isStopped)
        {
            throw new ObjectDisposedException(nameof(SwarmQueen), "The coordinator has been stopped.");
        }
    }

    private static string ReadBoundUrl(WebApplication application, string requestedUrl)
    {
        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>();

        if (addresses != null)
        {
            foreach (var address in addresses.Addresses)
            {
                if (!string.IsNullOrWhiteSpace(address))
                {
                    return address.TrimEnd('/');
                }
            }
        }

        return requestedUrl.Trim().TrimEnd('/');
    }
}
