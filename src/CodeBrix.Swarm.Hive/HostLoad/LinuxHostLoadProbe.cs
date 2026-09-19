using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Measures a Debian-based Linux host by reading the two files the kernel publishes its own figures
/// in. Nothing is called and nothing is installed: the kernel writes them, and reading them costs
/// almost nothing.
/// </summary>
/// <remarks>
/// The arithmetic and the parsing live in <see cref="LinuxMemInfo" />, <see cref="LinuxProcStat" />
/// and <see cref="CpuBusyMath" />, which is where they are checked. What is left here is opening two
/// files and remembering the previous processor totals.
/// </remarks>
internal sealed class LinuxHostLoadProbe : IHostLoadProbe
{
    private readonly string _memInfoPath;
    private readonly string _statPath;

    private ulong _previousBusyTicks;
    private ulong _previousTotalTicks;
    private bool _havePreviousTicks;

    public LinuxHostLoadProbe()
        : this(LinuxMemInfo.Path, LinuxProcStat.Path) { }

    public LinuxHostLoadProbe(string memInfoPath, string statPath)
    {
        _memInfoPath = memInfoPath;
        _statPath = statPath;
    }

    /// <inheritdoc />
    public async Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken)
    {
        long totalBytes;
        long availableBytes;

        var memInfo = await ReadTextAsync(_memInfoPath, cancellationToken).ConfigureAwait(false);

        if (memInfo == null || !LinuxMemInfo.TryParse(memInfo, out totalBytes, out availableBytes))
        {
            return HostLoadReading.Unknown;
        }

        var busyPercent = 0d;
        var stat = await ReadTextAsync(_statPath, cancellationToken).ConfigureAwait(false);

        if (stat != null && LinuxProcStat.TryParseTotals(stat, out var busyTicks, out var totalTicks))
        {
            if (_havePreviousTicks)
            {
                busyPercent = LinuxProcStat.BusyPercent(
                    _previousBusyTicks, _previousTotalTicks, busyTicks, totalTicks);
            }

            _previousBusyTicks = busyTicks;
            _previousTotalTicks = totalTicks;
            _havePreviousTicks = true;
        }

        return new HostLoadReading(totalBytes, availableBytes, busyPercent);
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            //These files are produced by the kernel as they are read, and report a length of zero, so
            //they are read whole rather than by length.
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            //A host whose kernel files cannot be read cannot be measured, and a Hive that cannot
            //measure its host does not start Workers on it.
            return null;
        }
    }
}
