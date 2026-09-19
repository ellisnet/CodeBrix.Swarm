using System;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Picks the way this host is measured. There is one implementation per operating system and the
/// choice is made by asking which one this is - never by looking for a library or catching a failure
/// from the wrong call.
/// </summary>
internal static class HostLoadProbes
{
    /// <summary>
    /// The probe for the operating system this process is running on.
    /// </summary>
    /// <returns>A probe that has taken no readings yet.</returns>
    /// <exception cref="PlatformNotSupportedException">
    /// The operating system is not one of the three the swarm's packages support.
    /// </exception>
    public static IHostLoadProbe ForThisHost()
    {
        if (OperatingSystem.IsLinux())
        {
            return new LinuxHostLoadProbe();
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsHostLoadProbe();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacOsHostLoadProbe();
        }

        throw new PlatformNotSupportedException(
            "A Hive measures the host it runs on, and it knows how to do that on Debian-based Linux, "
            + "on Windows and on macOS. This operating system is none of those.");
    }
}
