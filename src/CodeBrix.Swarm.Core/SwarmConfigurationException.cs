using System;

namespace CodeBrix.Swarm.Core;

/// <summary>
/// Thrown when something a swarm was handed to start from cannot be used: a Worker configuration
/// that is not valid JSON or is missing a required field, or an options object whose values do not
/// make sense together.
/// </summary>
public sealed class SwarmConfigurationException : Exception
{
    /// <summary>
    /// Creates the exception with a message describing what was wrong.
    /// </summary>
    /// <param name="message">What was wrong, and what a valid value looks like.</param>
    public SwarmConfigurationException(string message)
        : base(message) { }

    /// <summary>
    /// Creates the exception with a message and the lower-level failure behind it.
    /// </summary>
    /// <param name="message">What was wrong, and what a valid value looks like.</param>
    /// <param name="innerException">The failure that was caught, such as a JSON or file error.</param>
    public SwarmConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
