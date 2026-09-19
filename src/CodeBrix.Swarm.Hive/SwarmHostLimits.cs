using System;
using System.Globalization;
using CodeBrix.Swarm.Core;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// How much of the host a Hive is allowed to take up before it stops starting Workers. The defaults
/// are also CEILINGS: a consuming application may ask for less of the host than these allow, and
/// asking for more is refused rather than quietly clamped.
/// </summary>
/// <remarks>
/// <para>
/// These limits only ever stop NEW Workers from starting. A Hive never ends a Worker that is already
/// running because the host became busy - the work a Worker is in the middle of is worth more than
/// the headroom, and a host that is genuinely out of memory has the operating system's own answer to
/// that.
/// </para>
/// <para>
/// Three environment variables can lower these further on one host, which is how somebody who
/// operates a host holds back a machine that is shared with something else:
/// <c>CODEBRIX_SWARM_MAX_WORKERS</c>, <c>CODEBRIX_SWARM_MAX_RAM_PERCENT</c> and
/// <c>CODEBRIX_SWARM_MAX_CPU_PERCENT</c>. They can only lower what the application asked for; a
/// value that would raise it is ignored.
/// </para>
/// </remarks>
public sealed class SwarmHostLimits
{
    /// <summary>
    /// The most of the host's memory a Hive will ever let itself reach, as a percentage in use. It
    /// is both the default and the ceiling.
    /// </summary>
    public const double RamPercentCeiling = 90d;

    /// <summary>
    /// The most of the host's processor a Hive will ever let itself reach, as an average percentage
    /// busy over <see cref="CpuAveragingWindow" />. It is both the default and the ceiling.
    /// </summary>
    public const double CpuPercentCeiling = 90d;

    /// <summary>
    /// The default for <see cref="FreeRamFloorBytes" />: one gibibyte.
    /// </summary>
    public const long DefaultFreeRamFloorBytes = 1024L * 1024L * 1024L;

    /// <summary>The name of the environment variable that can lower <see cref="MaxWorkers" />.</summary>
    public const string MaxWorkersVariable = "CODEBRIX_SWARM_MAX_WORKERS";

    /// <summary>The name of the environment variable that can lower <see cref="MaxRamPercent" />.</summary>
    public const string MaxRamPercentVariable = "CODEBRIX_SWARM_MAX_RAM_PERCENT";

    /// <summary>The name of the environment variable that can lower <see cref="MaxCpuPercent" />.</summary>
    public const string MaxCpuPercentVariable = "CODEBRIX_SWARM_MAX_CPU_PERCENT";

    /// <summary>
    /// The default for <see cref="CpuAveragingWindow" />: half a minute.
    /// </summary>
    public static readonly TimeSpan DefaultCpuAveragingWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How much of the host's memory may be in use before no more Workers are started, as a
    /// percentage. It must be more than nothing and no more than
    /// <see cref="RamPercentCeiling" />.
    /// </summary>
    public double MaxRamPercent { get; set; } = RamPercentCeiling;

    /// <summary>
    /// How busy the host's processor may be before no more Workers are started, as a percentage
    /// averaged over <see cref="CpuAveragingWindow" />. It must be more than nothing and no more
    /// than <see cref="CpuPercentCeiling" />.
    /// </summary>
    /// <remarks>
    /// It is an average on purpose. A single reading of a host's processor says almost nothing - a
    /// perfectly idle machine reads as busy for the instant something wakes up on it.
    /// </remarks>
    public double MaxCpuPercent { get; set; } = CpuPercentCeiling;

    /// <summary>
    /// How much memory must be left free, in bytes, whatever <see cref="MaxRamPercent" /> allows. A
    /// percentage alone is not enough on a large host: a tenth of a very large machine still leaves
    /// room for trouble, and a tenth of a small one leaves none.
    /// </summary>
    public long FreeRamFloorBytes { get; set; } = DefaultFreeRamFloorBytes;

    /// <summary>
    /// How long the host's processor readings are averaged over before they are compared with
    /// <see cref="MaxCpuPercent" />.
    /// </summary>
    public TimeSpan CpuAveragingWindow { get; set; } = DefaultCpuAveragingWindow;

    /// <summary>
    /// The most Workers this Hive may have running at once, or null - the default - for no limit
    /// beyond what the host's memory and processor allow.
    /// </summary>
    public int? MaxWorkers { get; set; }

    /// <summary>
    /// Checks that the values are present, in range, and no higher than the ceilings.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">
    /// A value is out of range, or is above the ceiling for it.
    /// </exception>
    public void Validate()
    {
        if (double.IsNaN(MaxRamPercent) || MaxRamPercent <= 0d)
        {
            throw new SwarmConfigurationException(
                "The share of the host's memory a Hive may use must be more than nothing, and was "
                + MaxRamPercent.ToString(CultureInfo.InvariantCulture) + ".");
        }

        if (MaxRamPercent > RamPercentCeiling)
        {
            throw new SwarmConfigurationException(
                "The share of the host's memory a Hive may use must not be above "
                + RamPercentCeiling.ToString(CultureInfo.InvariantCulture) + " percent, and was "
                + MaxRamPercent.ToString(CultureInfo.InvariantCulture)
                + ". The default is the ceiling: a Hive may be asked to leave more of the host "
                + "alone, never less.");
        }

        if (double.IsNaN(MaxCpuPercent) || MaxCpuPercent <= 0d)
        {
            throw new SwarmConfigurationException(
                "The share of the host's processor a Hive may use must be more than nothing, and was "
                + MaxCpuPercent.ToString(CultureInfo.InvariantCulture) + ".");
        }

        if (MaxCpuPercent > CpuPercentCeiling)
        {
            throw new SwarmConfigurationException(
                "The share of the host's processor a Hive may use must not be above "
                + CpuPercentCeiling.ToString(CultureInfo.InvariantCulture) + " percent, and was "
                + MaxCpuPercent.ToString(CultureInfo.InvariantCulture)
                + ". The default is the ceiling: a Hive may be asked to leave more of the host "
                + "alone, never less.");
        }

        if (FreeRamFloorBytes < 0L)
        {
            throw new SwarmConfigurationException(
                "The amount of memory a Hive must leave free cannot be negative.");
        }

        if (CpuAveragingWindow <= TimeSpan.Zero)
        {
            throw new SwarmConfigurationException(
                "The window the host's processor readings are averaged over must be longer than "
                + "nothing.");
        }

        if (MaxWorkers.HasValue && MaxWorkers.Value < 1)
        {
            throw new SwarmConfigurationException(
                "The most Workers a Hive may run at once must be at least one, or left unset for no "
                + "limit beyond what the host allows.");
        }
    }

    /// <summary>
    /// Returns a copy of these limits with the same values, so that the one a Hive runs with cannot
    /// be changed underneath it while it is running.
    /// </summary>
    /// <returns>A new instance with the same values.</returns>
    public SwarmHostLimits Copy() => new()
    {
        MaxRamPercent = MaxRamPercent,
        MaxCpuPercent = MaxCpuPercent,
        FreeRamFloorBytes = FreeRamFloorBytes,
        CpuAveragingWindow = CpuAveragingWindow,
        MaxWorkers = MaxWorkers
    };
}
