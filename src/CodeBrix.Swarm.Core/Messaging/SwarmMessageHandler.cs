using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Core.Messaging;

/// <summary>
/// What a consuming application registers to be told about one kind of message.
/// </summary>
/// <param name="message">The message that arrived.</param>
/// <param name="cancellationToken">
/// Cancelled when the process the handler belongs to is shutting down.
/// </param>
/// <returns>A task that completes when the handler is done with the message.</returns>
/// <remarks>
/// A handler is awaited before the next message of the same kind is delivered, so a slow handler
/// holds up that kind. Anything a handler throws is reported to the owning Hive or Worker and does
/// not close the connection.
/// </remarks>
public delegate Task SwarmMessageHandler(SwarmMessage message, CancellationToken cancellationToken);
