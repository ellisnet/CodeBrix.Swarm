using System;

namespace CodeBrix.Swarm.Core.Connections;

/// <summary>
/// What a Hive or a Worker needs in order to reach the coordinator, and how long it keeps trying
/// when it cannot. A Hive's and a Worker's own options objects pass their values through to one of
/// these, so the retry behaviour is the same for both.
/// </summary>
internal sealed class SwarmConnectionOptions
{
    /// <summary>
    /// The default for <see cref="QueenUnreachableWindow" />: a minute.
    /// </summary>
    public static readonly TimeSpan DefaultQueenUnreachableWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The coordinator's base address, which must be an absolute http or https address. The hub path
    /// for the role is appended to it.
    /// </summary>
    public string QueenUrl { get; set; }

    /// <summary>
    /// The access token for this process's role.
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// How long to keep trying to reach the coordinator before giving up - at start-up and again
    /// after losing the connection. When it runs out, the connection reports that the coordinator is
    /// out of reach and the Hive or Worker winds itself down.
    /// </summary>
    public TimeSpan QueenUnreachableWindow { get; set; } = DefaultQueenUnreachableWindow;

    /// <summary>
    /// How long to wait after the first attempt fails. Each further wait is twice the last, up to
    /// <see cref="MaximumRetryDelay" />, so the first few attempts come quickly and the rest settle
    /// into a steady rhythm.
    /// </summary>
    public TimeSpan FirstRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The longest wait between attempts.
    /// </summary>
    public TimeSpan MaximumRetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Checks that the values are present and make sense together.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">
    /// Something is missing or out of range, or the address is not an absolute http or https address.
    /// </exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(QueenUrl))
        {
            throw new SwarmConfigurationException(
                "The coordinator's base address is missing.");
        }

        //AN ABSOLUTE ADDRESS IS NOT ENOUGH. On Linux a path such as "/swarm/hive" parses perfectly
        //well as an absolute file address, so a client that only asked for "absolute" would accept a
        //value it could never connect to and then spend its whole retry window failing to.
        if (!Uri.TryCreate(QueenUrl.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new SwarmConfigurationException(
                $"The coordinator's base address must be an absolute http or https address, and was "
                + $"'{QueenUrl}'.");
        }

        if (string.IsNullOrWhiteSpace(Token))
        {
            throw new SwarmConfigurationException(
                "The access token is missing. Only the coordinator can mint one.");
        }

        if (QueenUnreachableWindow < TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The window for reaching the coordinator must not be negative.");
        }

        if (FirstRetryDelay <= TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The first retry delay must be longer than nothing.");
        }

        if (MaximumRetryDelay < FirstRetryDelay)
        {
            throw new SwarmConfigurationException(
                "The longest retry delay must not be shorter than the first one.");
        }
    }
}
