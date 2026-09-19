using System;
using System.Text.Json;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Worker;

/// <summary>
/// What a Worker knows about itself, and the only thing the consuming application's work is handed.
/// </summary>
public sealed class SwarmWorkerContext
{
    private readonly SwarmMessageDispatcher _messages;

    internal SwarmWorkerContext(
        string workerId,
        string work,
        bool isDevelopmentMode,
        SwarmMessageDispatcher messages)
    {
        WorkerId = workerId;
        Work = work;
        IsDevelopmentMode = isDevelopmentMode;
        _messages = messages;
    }

    /// <summary>
    /// What this Worker is called. The Hive that started it gave every Worker a different one.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// The application's own part of the configuration, as the JSON text it was written as. The
    /// swarm carried it here without reading it. Use <see cref="GetWork{TWork}" /> to read it into
    /// a type, or read the text directly when the application does its own parsing.
    /// </summary>
    public string Work { get; }

    /// <summary>
    /// True when this Worker was started by hand from a configuration file rather than by a Hive.
    /// There is no lifeline in that case, so nothing ends the Worker when a Hive goes away.
    /// </summary>
    public bool IsDevelopmentMode { get; }

    /// <summary>
    /// Why the Worker is stopping. It is meaningless until the work has ended; the shutdown step is
    /// where it is worth reading.
    /// </summary>
    public SwarmWorkerOutcome Outcome { get; internal set; }

    /// <summary>
    /// Reads the application's own part of the configuration into a type of its own.
    /// </summary>
    /// <typeparam name="TWork">The type the Hive's side wrote it from.</typeparam>
    /// <returns>The value, or the type's default when there was no application part.</returns>
    /// <exception cref="JsonException">The text is not valid JSON for the requested type.</exception>
    public TWork GetWork<TWork>()
    {
        if (string.IsNullOrWhiteSpace(Work))
        {
            return default;
        }

        return JsonSerializer.Deserialize<TWork>(Work, SwarmJson.Options);
    }

    /// <summary>
    /// Registers what to do when the coordinator sends a message of one application-defined kind.
    /// Register from the configure step, before the Worker connects, so that nothing sent in the
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
                $"'{kind}' is one of the swarm's own message kinds; the Worker library already "
                + "handles it.",
                nameof(kind));
        }

        _messages.Register(kind, handler);
    }
}
