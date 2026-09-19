using System;

namespace CodeBrix.Swarm.Queen.Authentication;

/// <summary>
/// Thrown when a token cannot be minted or cannot be accepted: a master secret that is missing or
/// too short, or a token that is malformed, is for the other role, has expired, or has been altered.
/// </summary>
public sealed class SwarmTokenException : Exception
{
    /// <summary>
    /// Creates the exception with a message describing what was wrong with the token.
    /// </summary>
    /// <param name="message">What was wrong. It never contains the token or the secret.</param>
    public SwarmTokenException(string message)
        : base(message) { }

    /// <summary>
    /// Creates the exception with a message and the lower-level failure behind it.
    /// </summary>
    /// <param name="message">What was wrong. It never contains the token or the secret.</param>
    /// <param name="innerException">The failure that was caught, such as a decoding error.</param>
    public SwarmTokenException(string message, Exception innerException)
        : base(message, innerException) { }
}
