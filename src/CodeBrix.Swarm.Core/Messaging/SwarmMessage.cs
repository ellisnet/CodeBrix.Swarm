using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.Swarm.Core.Messaging;

/// <summary>
/// The envelope every message in a swarm travels in. The coordinator sends one of these to every
/// Hive or every Worker; the receiving side looks at <see cref="Kind" /> to decide which handler,
/// if any, wants it, and reads <see cref="Payload" /> through
/// <see cref="GetPayload{TPayload}" /> when it does.
/// </summary>
/// <remarks>
/// The envelope itself is fixed; everything a consuming application wants to say goes in the
/// payload, which is JSON and is never read by the swarm.
/// </remarks>
public sealed class SwarmMessage
{
    /// <summary>
    /// A new identifier for every message, for correlating what was sent with what a receiver
    /// reports having done.
    /// </summary>
    [JsonPropertyName("id")]
    public Guid MessageId { get; set; }

    /// <summary>
    /// When the message was created, in UTC. A receiver's own clock may differ; this is the
    /// coordinator's reading.
    /// </summary>
    [JsonPropertyName("sentUtc")]
    public DateTime SentUtc { get; set; }

    /// <summary>
    /// What kind of message this is. A receiver registers one handler per kind, and a kind nobody
    /// registered for is ignored. The swarm's own kinds are on
    /// <see cref="SwarmMessageKinds" />; every other value belongs to the consuming application.
    /// Comparison is exact and case-sensitive.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; }

    /// <summary>
    /// The message body as JSON text, or null when the kind carries no body. Use
    /// <see cref="Create{TPayload}" /> and <see cref="GetPayload{TPayload}" /> rather than setting
    /// and reading this directly, unless the application does its own serializing.
    /// </summary>
    [JsonPropertyName("payload")]
    public string Payload { get; set; }

    /// <summary>
    /// Creates an empty envelope. The swarm's hub protocol uses this when it reads a message off the
    /// wire; application code should use <see cref="Create(string)" /> or
    /// <see cref="Create{TPayload}" />.
    /// </summary>
    public SwarmMessage() { }

    /// <summary>
    /// True when the message has a body to read.
    /// </summary>
    [JsonIgnore]
    public bool HasPayload => !string.IsNullOrWhiteSpace(Payload);

    /// <summary>
    /// Creates a message of the given kind with no body, stamped with a new identifier and the
    /// current UTC time.
    /// </summary>
    /// <param name="kind">The kind the receiving side registers a handler for.</param>
    /// <returns>The new message.</returns>
    /// <exception cref="ArgumentException">The kind is null, empty or whitespace.</exception>
    public static SwarmMessage Create(string kind)
    {
        RequireKind(kind);

        return new SwarmMessage
        {
            MessageId = Guid.NewGuid(),
            SentUtc = DateTime.UtcNow,
            Kind = kind,
            Payload = null
        };
    }

    /// <summary>
    /// Creates a message of the given kind, serializing the body to JSON.
    /// </summary>
    /// <typeparam name="TPayload">The application's own body type.</typeparam>
    /// <param name="kind">The kind the receiving side registers a handler for.</param>
    /// <param name="payload">The body. A null body produces a message with no body.</param>
    /// <returns>The new message.</returns>
    /// <exception cref="ArgumentException">The kind is null, empty or whitespace.</exception>
    /// <exception cref="JsonException">The body could not be serialized.</exception>
    public static SwarmMessage Create<TPayload>(string kind, TPayload payload)
    {
        RequireKind(kind);

        return new SwarmMessage
        {
            MessageId = Guid.NewGuid(),
            SentUtc = DateTime.UtcNow,
            Kind = kind,
            Payload = payload == null ? null : JsonSerializer.Serialize(payload, SwarmJson.Options)
        };
    }

    /// <summary>
    /// Reads the body back into the application's own type.
    /// </summary>
    /// <typeparam name="TPayload">The type the body was written from.</typeparam>
    /// <returns>
    /// The body, or the type's default value when the message has none.
    /// </returns>
    /// <exception cref="JsonException">The body is not valid JSON for the requested type.</exception>
    public TPayload GetPayload<TPayload>()
    {
        if (!HasPayload)
        {
            return default;
        }

        return JsonSerializer.Deserialize<TPayload>(Payload, SwarmJson.Options);
    }

    private static void RequireKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            throw new ArgumentException("A message kind must not be null or whitespace.", nameof(kind));
        }
    }
}
