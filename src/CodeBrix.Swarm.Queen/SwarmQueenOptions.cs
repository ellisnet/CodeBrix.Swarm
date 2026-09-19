using System;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Queen.Authentication;
using Microsoft.Extensions.Logging;

namespace CodeBrix.Swarm.Queen;

/// <summary>
/// What the consuming application hands the coordinator when it starts it: where to listen and the
/// one secret every access token in the swarm is derived from.
/// </summary>
public sealed class SwarmQueenOptions
{
    /// <summary>
    /// An address that asks the operating system for any free port on the loopback interface. The
    /// port that was actually taken is then readable from <see cref="SwarmQueen.BaseUrl" />. It is
    /// what a test should use, so that two runs at once cannot collide.
    /// </summary>
    public const string AnyFreeLoopbackPortUrl = "http://127.0.0.1:0";

    /// <summary>
    /// The address the coordinator listens on, such as <c>http://0.0.0.0:5000</c>. A port of zero
    /// means any free port.
    /// </summary>
    /// <remarks>
    /// Plain HTTP is what a swarm on a trusted local network normally uses, and the access token
    /// then travels in the clear. An <c>https</c> address is not blocked: supply one, with the
    /// certificate configuration the ASP.NET Core host needs, and it is used as given.
    /// </remarks>
    public string Url { get; set; } = AnyFreeLoopbackPortUrl;

    /// <summary>
    /// The secret every access token in this swarm is derived from. It must be at least
    /// <see cref="SwarmTokenAuthority.MinimumMasterSecretLength" /> characters. It is never
    /// compiled into this library, never sent anywhere, and never leaves the coordinator: a Hive or
    /// a Worker only ever holds an opaque token minted from it.
    /// </summary>
    public string MasterSecret { get; set; }

    /// <summary>
    /// Where the coordinator's own web host should send its logging, or null for no logging at all.
    /// A desktop application that owns a coordinator usually wants its own factory here rather than
    /// a console it does not have.
    /// </summary>
    public ILoggerFactory LoggerFactory { get; set; }

    /// <summary>
    /// Checks that the values are present and make sense.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">
    /// The address is missing or is not an absolute HTTP or HTTPS address.
    /// </exception>
    /// <exception cref="SwarmTokenException">
    /// The master secret is missing or too short.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new SwarmConfigurationException(
                "The coordinator has no address to listen on.");
        }

        if (!Uri.TryCreate(Url.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new SwarmConfigurationException(
                $"The coordinator's address must be an absolute http or https address, and was '{Url}'.");
        }

        if (string.IsNullOrWhiteSpace(MasterSecret))
        {
            throw new SwarmTokenException(
                "The swarm's master secret is missing. The consuming application supplies it; it is "
                + "never built into this library.");
        }

        if (MasterSecret.Trim().Length < SwarmTokenAuthority.MinimumMasterSecretLength)
        {
            throw new SwarmTokenException(
                $"The swarm's master secret is too short: it must be at least "
                + $"{SwarmTokenAuthority.MinimumMasterSecretLength} characters, and was "
                + $"{MasterSecret.Trim().Length}.");
        }
    }
}
