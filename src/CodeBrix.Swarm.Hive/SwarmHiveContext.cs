using System;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// What a Hive knows about itself, and the only thing the consuming application's steps are handed:
/// the step that sets the Hive up, the one that is asked what to start next, and the one that runs on
/// the way out.
/// </summary>
public sealed class SwarmHiveContext
{
    private readonly SwarmMessageDispatcher _messages;
    private readonly Func<int> _runningWorkerCount;
    private readonly Func<int> _startedWorkerCount;
    private readonly Func<bool> _isConnected;

    internal SwarmHiveContext(
        string hiveId,
        SwarmMessageDispatcher messages,
        Func<int> runningWorkerCount,
        Func<int> startedWorkerCount,
        Func<bool> isConnected)
    {
        HiveId = hiveId;
        _messages = messages;
        _runningWorkerCount = runningWorkerCount;
        _startedWorkerCount = startedWorkerCount;
        _isConnected = isConnected;
    }

    /// <summary>
    /// What this Hive is called. Every Worker it starts is named after it, so two Hives that share a
    /// name would give their Workers the same names.
    /// </summary>
    public string HiveId { get; }

    /// <summary>How many Workers this Hive has running at this moment.</summary>
    public int RunningWorkerCount => _runningWorkerCount();

    /// <summary>
    /// How many Workers this Hive has started altogether, including the ones that have since ended.
    /// </summary>
    public int StartedWorkerCount => _startedWorkerCount();

    /// <summary>
    /// True while the Hive's connection to the coordinator is open. A Hive whose connection has
    /// dropped keeps its Workers running and starts no new ones while it tries to get back.
    /// </summary>
    public bool IsConnected => _isConnected();

    /// <summary>
    /// Why the Hive is finishing. It is meaningless until the run has ended; the step that runs on
    /// the way out is where it is worth reading.
    /// </summary>
    public SwarmHiveOutcome Outcome { get; internal set; }

    /// <summary>
    /// Registers what to do when the coordinator sends a message of one application-defined kind.
    /// Register from the step that sets the Hive up, before it connects, so that nothing sent in the
    /// first moments is missed. A kind nothing is registered for is ignored.
    /// </summary>
    /// <param name="kind">
    /// The kind to listen for. It must not be one of the kinds the swarm reserves - those are
    /// handled by the library itself.
    /// </param>
    /// <param name="handler">What to do with a message of that kind.</param>
    /// <exception cref="ArgumentException">The kind is missing or is reserved.</exception>
    /// <exception cref="ArgumentNullException">The handler is null.</exception>
    public void RegisterHandler(string kind, SwarmMessageHandler handler)
    {
        if (SwarmMessageKinds.IsBuiltIn(kind))
        {
            throw new ArgumentException(
                $"'{kind}' is one of the swarm's own message kinds; the Hive library already handles "
                + "it.",
                nameof(kind));
        }

        _messages.Register(kind, handler);
    }
}
