using System;
using System.Globalization;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Reads the two figures a Hive needs out of the text the Linux kernel publishes about memory.
/// </summary>
/// <remarks>
/// <para>
/// THE FIGURE TO USE IS MemAvailable, NOT MemFree. A healthy Linux host has almost no free memory:
/// the kernel fills what nothing else wants with page cache, and gives it straight back when
/// something asks. A Hive that looked at MemFree would decide every working host was out of memory.
/// MemAvailable is the kernel's own estimate of what could be handed out without pushing the host
/// into swapping, which is exactly the question being asked.
/// </para>
/// <para>
/// The parsing is separate from the reading so that it can be checked against real text from real
/// hosts without one.
/// </para>
/// </remarks>
internal static class LinuxMemInfo
{
    /// <summary>Where the kernel publishes it.</summary>
    public const string Path = "/proc/meminfo";

    private const string TotalLabel = "MemTotal:";
    private const string AvailableLabel = "MemAvailable:";

    /// <summary>
    /// Reads the total and available memory out of the file's text.
    /// </summary>
    /// <param name="text">The whole contents of the file.</param>
    /// <param name="totalBytes">How much memory the host has altogether.</param>
    /// <param name="availableBytes">How much of it could be handed out.</param>
    /// <returns>True when both figures were found.</returns>
    public static bool TryParse(string text, out long totalBytes, out long availableBytes)
    {
        totalBytes = 0L;
        availableBytes = 0L;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var haveTotal = false;
        var haveAvailable = false;

        foreach (var line in text.Split('\n'))
        {
            if (!haveTotal && TryReadLabelledValue(line, TotalLabel, out var total))
            {
                totalBytes = total;
                haveTotal = true;
                continue;
            }

            if (!haveAvailable && TryReadLabelledValue(line, AvailableLabel, out var available))
            {
                availableBytes = available;
                haveAvailable = true;
            }

            if (haveTotal && haveAvailable)
            {
                break;
            }
        }

        if (!haveTotal || !haveAvailable || totalBytes <= 0L || availableBytes < 0L)
        {
            totalBytes = 0L;
            availableBytes = 0L;
            return false;
        }

        if (availableBytes > totalBytes)
        {
            availableBytes = totalBytes;
        }

        return true;
    }

    /// <summary>
    /// Reads one labelled line, such as <c>MemTotal:       16316148 kB</c>, and returns the value in
    /// bytes. The kernel writes these in kibibytes and says so; a line with no unit is taken as bytes.
    /// </summary>
    /// <param name="line">One line of the file.</param>
    /// <param name="label">The label to look for, including its colon.</param>
    /// <param name="bytes">The value, in bytes.</param>
    /// <returns>True when the line carried that label and a number.</returns>
    public static bool TryReadLabelledValue(string line, string label, out long bytes)
    {
        bytes = 0L;

        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(label))
        {
            return false;
        }

        var trimmed = line.Trim();

        if (!trimmed.StartsWith(label, StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = trimmed[label.Length..].Trim();

        if (remainder.Length == 0)
        {
            return false;
        }

        var multiplier = 1L;
        var numberEnd = 0;

        while (numberEnd < remainder.Length && char.IsAsciiDigit(remainder[numberEnd]))
        {
            numberEnd++;
        }

        if (numberEnd == 0)
        {
            return false;
        }

        var unit = remainder[numberEnd..].Trim();

        if (unit.Length > 0)
        {
            if (unit.Equals("kB", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1024L;
            }
            else if (unit.Equals("MB", StringComparison.OrdinalIgnoreCase))
            {
                multiplier = 1024L * 1024L;
            }
            else
            {
                //A unit nobody has seen on this line before. Rather than guess at a scale, refuse it.
                return false;
            }
        }

        if (!long.TryParse(
                remainder[..numberEnd],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return false;
        }

        bytes = value * multiplier;
        return true;
    }
}
