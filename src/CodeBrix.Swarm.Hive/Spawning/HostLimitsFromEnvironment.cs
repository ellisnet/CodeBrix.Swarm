using System;
using System.Globalization;

namespace CodeBrix.Swarm.Hive.Spawning;

/// <summary>
/// Lets whoever looks after a host hold a Hive back further than the application asked, through three
/// environment variables.
/// </summary>
/// <remarks>
/// <para>
/// THEY CAN ONLY LOWER. Someone who operates a host knows things the application does not - that this
/// machine is shared with something else, that it is somebody's desk - and needs a way to say "not so
/// much here" without changing the application. What they must NOT be able to do is talk a Hive into
/// taking more of a host than the application considered safe, so a value that would raise a limit is
/// read, found to be higher, and ignored.
/// </para>
/// <para>
/// A variable that is not set, is empty, or cannot be read as a number is ignored in the same way. A
/// misspelt value holds nothing back, which is the safe direction to be wrong in.
/// </para>
/// </remarks>
internal static class HostLimitsFromEnvironment
{
    /// <summary>
    /// Applies the environment's variables to a copy of the limits, lowering them where the
    /// environment asks for less.
    /// </summary>
    /// <param name="limits">The limits the application asked for.</param>
    /// <param name="readVariable">
    /// How to read one environment variable by name. A test hands over its own.
    /// </param>
    /// <returns>A new set of limits, no higher than the ones handed in.</returns>
    public static SwarmHostLimits Apply(SwarmHostLimits limits, Func<string, string> readVariable)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(readVariable);

        var lowered = limits.Copy();

        if (TryReadPercent(readVariable, SwarmHostLimits.MaxRamPercentVariable, out var ramPercent)
            && ramPercent < lowered.MaxRamPercent)
        {
            lowered.MaxRamPercent = ramPercent;
        }

        if (TryReadPercent(readVariable, SwarmHostLimits.MaxCpuPercentVariable, out var cpuPercent)
            && cpuPercent < lowered.MaxCpuPercent)
        {
            lowered.MaxCpuPercent = cpuPercent;
        }

        if (TryReadWorkerCount(readVariable, out var maxWorkers)
            && (!lowered.MaxWorkers.HasValue || maxWorkers < lowered.MaxWorkers.Value))
        {
            lowered.MaxWorkers = maxWorkers;
        }

        return lowered;
    }

    private static bool TryReadPercent(Func<string, string> readVariable, string name, out double percent)
    {
        percent = 0d;

        var text = Read(readVariable, name);

        if (text == null)
        {
            return false;
        }

        if (!double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            return false;
        }

        if (double.IsNaN(parsed) || parsed <= 0d)
        {
            return false;
        }

        percent = parsed;
        return true;
    }

    private static bool TryReadWorkerCount(Func<string, string> readVariable, out int count)
    {
        count = 0;

        var text = Read(readVariable, SwarmHostLimits.MaxWorkersVariable);

        if (text == null)
        {
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            || parsed < 1)
        {
            return false;
        }

        count = parsed;
        return true;
    }

    private static string Read(Func<string, string> readVariable, string name)
    {
        string text;

        try
        {
            text = readVariable(name);
        }
        catch (Exception)
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
