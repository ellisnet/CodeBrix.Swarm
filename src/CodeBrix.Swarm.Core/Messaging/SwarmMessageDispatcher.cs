using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Core.Messaging;

/// <summary>
/// Holds the handlers a Hive or a Worker has registered and hands each arriving message to the ones
/// that asked for its kind. A message of a kind nobody registered for is ignored, quietly and on
/// purpose: the coordinator sends to everything connected, and most of what it sends is not for
/// every listener.
/// </summary>
/// <remarks>
/// Registering and dispatching are safe to do from several threads at once. Kinds are compared
/// exactly and case-sensitively.
/// </remarks>
public sealed class SwarmMessageDispatcher
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<SwarmMessageHandler>> _handlers =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Adds a handler for one kind of message. More than one handler may be registered for the same
    /// kind; they are called in the order they were registered.
    /// </summary>
    /// <param name="kind">The kind of message the handler wants.</param>
    /// <param name="handler">What to do with a message of that kind.</param>
    /// <exception cref="ArgumentException">The kind is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    public void Register(string kind, SwarmMessageHandler handler)
    {
        RequireKind(kind);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(kind, out var registered))
            {
                registered = [];
                _handlers[kind] = registered;
            }

            registered.Add(handler);
        }
    }

    /// <summary>
    /// Removes one previously registered handler.
    /// </summary>
    /// <param name="kind">The kind the handler was registered for.</param>
    /// <param name="handler">The handler to remove.</param>
    /// <returns>True when that handler was registered and has been removed.</returns>
    /// <exception cref="ArgumentException">The kind is null, empty or whitespace.</exception>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    public bool Unregister(string kind, SwarmMessageHandler handler)
    {
        RequireKind(kind);
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(kind, out var registered))
            {
                return false;
            }

            var removed = registered.Remove(handler);

            if (registered.Count == 0)
            {
                _handlers.Remove(kind);
            }

            return removed;
        }
    }

    /// <summary>
    /// True when at least one handler is registered for the kind.
    /// </summary>
    /// <param name="kind">The kind to look for.</param>
    /// <returns>True when something would receive a message of that kind.</returns>
    public bool IsRegistered(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return false;
        }

        lock (_gate)
        {
            return _handlers.TryGetValue(kind, out var registered) && registered.Count > 0;
        }
    }

    /// <summary>
    /// Hands the message to every handler registered for its kind, in registration order.
    /// </summary>
    /// <param name="message">The message that arrived.</param>
    /// <param name="cancellationToken">Cancelled when the owning process is shutting down.</param>
    /// <returns>
    /// A task that completes once every handler has finished. It completes immediately when nothing
    /// is registered for the kind.
    /// </returns>
    /// <exception cref="ArgumentNullException">The message is null.</exception>
    /// <exception cref="AggregateException">
    /// One or more handlers threw. Every handler is still called; the failures are collected and
    /// reported together.
    /// </exception>
    public async Task DispatchAsync(SwarmMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        SwarmMessageHandler[] handlers;

        lock (_gate)
        {
            if (message.Kind == null
                || !_handlers.TryGetValue(message.Kind, out var registered)
                || registered.Count == 0)
            {
                return;
            }

            handlers = registered.ToArray();
        }

        List<Exception> failures = null;

        foreach (var handler in handlers)
        {
            try
            {
                await handler(message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failures ??= [];
                failures.Add(ex);
            }
        }

        if (failures != null)
        {
            throw new AggregateException(
                $"{failures.Count} handler(s) of swarm message kind '{message.Kind}' threw.",
                failures);
        }
    }

    private static void RequireKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("A message kind must not be null or whitespace.", nameof(kind));
        }
    }
}
