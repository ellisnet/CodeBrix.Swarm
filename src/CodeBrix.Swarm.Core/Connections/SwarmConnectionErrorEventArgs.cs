using System;

namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// Something the connection dealt with by itself and carried on from: an attempt that failed while
/// the retry window was still open, a connection that dropped, or a message handler that threw.
/// Nothing here ends the Hive or the Worker; it is reported so that a consuming application can log
/// it.
/// </summary>
internal sealed class SwarmConnectionErrorEventArgs : EventArgs
{
    public SwarmConnectionErrorEventArgs(string context, Exception error)
    {
        Context = context;
        Error = error;
    }

    /// <summary>
    /// A few words about what was being done, such as "connect" or "handling 'swarm.terminate-worker'".
    /// </summary>
    public string Context { get; }

    /// <summary>
    /// What was caught. Null when a connection closed without a failure.
    /// </summary>
    public Exception Error { get; }
}
