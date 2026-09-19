using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Measures a macOS host through the operating system's own calls: one system setting for the amount
/// of memory installed, and two kernel statistics calls for how that memory is being used and how
/// busy the processors have been. Nothing is installed and nothing is built: all of it lives in the
/// library every macOS process already has loaded.
/// </summary>
/// <remarks>
/// <para>
/// The three calls are <c>sysctlbyname("hw.memsize")</c> for the total,
/// <c>host_statistics64(HOST_VM_INFO64)</c> for the page counts memory use is worked out from, and
/// <c>host_statistics(HOST_CPU_LOAD_INFO)</c> for the processor tick counters. The page size comes
/// from <c>host_page_size</c> rather than being assumed, because it is not the same on every Mac.
/// </para>
/// <para>
/// The arithmetic is in <see cref="MacOsHostStatistics" />, where it is checked without macOS
/// underneath it.
/// </para>
/// </remarks>
internal sealed class MacOsHostLoadProbe : IHostLoadProbe
{
    private const string LibSystem = "libSystem.dylib";

    private const int KernelSuccess = 0;

    //From the operating system's own headers: the numbers that say which set of statistics is wanted.
    private const int HostVmInfo64 = 4;
    private const int HostCpuLoadInfo = 3;

    private long _totalBytes;
    private long _pageSizeBytes;
    private uint _hostPort;
    private bool _haveHost;

    private MacOsCpuTicks _previousTicks;
    private bool _havePreviousTicks;

    /// <inheritdoc />
    public Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Read());
    }

    private HostLoadReading Read()
    {
        try
        {
            if (!EnsureHostFacts())
            {
                return HostLoadReading.Unknown;
            }

            var statistics = default(VmStatistics64);
            var count = (uint)(Marshal.SizeOf<VmStatistics64>() / sizeof(uint));

            if (host_statistics64(_hostPort, HostVmInfo64, ref statistics, ref count) != KernelSuccess)
            {
                return HostLoadReading.Unknown;
            }

            var availableBytes = MacOsHostStatistics.AvailableBytes(
                _totalBytes,
                _pageSizeBytes,
                statistics.InternalPageCount,
                statistics.PurgeableCount,
                statistics.WireCount,
                statistics.CompressorPageCount);

            var busyPercent = ReadBusyPercent();

            return new HostLoadReading(_totalBytes, availableBytes, busyPercent);
        }
        catch (Exception)
        {
            return HostLoadReading.Unknown;
        }
    }

    private double ReadBusyPercent()
    {
        var load = default(CpuLoadInfo);
        var count = (uint)(Marshal.SizeOf<CpuLoadInfo>() / sizeof(uint));

        if (host_statistics(_hostPort, HostCpuLoadInfo, ref load, ref count) != KernelSuccess)
        {
            return 0d;
        }

        var ticks = new MacOsCpuTicks(load.User, load.System, load.Idle, load.Nice);
        var busyPercent = 0d;

        if (_havePreviousTicks)
        {
            busyPercent = MacOsHostStatistics.BusyPercent(_previousTicks, ticks);
        }

        _previousTicks = ticks;
        _havePreviousTicks = true;

        return busyPercent;
    }

    private bool EnsureHostFacts()
    {
        if (_haveHost)
        {
            return true;
        }

        //The host port is asked for once and kept. Asking again hands out another reference to the
        //same thing, and a probe that asked on every reading would accumulate them.
        var port = mach_host_self();

        if (port == 0U)
        {
            return false;
        }

        if (host_page_size(port, out var pageSize) != KernelSuccess)
        {
            return false;
        }

        var pageSizeBytes = pageSize.ToInt64();

        if (pageSizeBytes <= 0L)
        {
            return false;
        }

        if (!TryReadTotalMemory(out var totalBytes))
        {
            return false;
        }

        _hostPort = port;
        _pageSizeBytes = pageSizeBytes;
        _totalBytes = totalBytes;
        _haveHost = true;

        return true;
    }

    private static bool TryReadTotalMemory(out long totalBytes)
    {
        totalBytes = 0L;

        //The name is handed over as bytes with the ending zero the C library expects, rather than as
        //a string, so that nothing has to be assumed about how text is turned into bytes.
        var name = Encoding.UTF8.GetBytes("hw.memsize\0");
        var value = 0L;
        var length = (IntPtr)sizeof(long);

        if (sysctlbyname(name, ref value, ref length, IntPtr.Zero, IntPtr.Zero) != 0)
        {
            return false;
        }

        if (value <= 0L)
        {
            return false;
        }

        totalBytes = value;
        return true;
    }

    /// <summary>
    /// The shape the kernel fills in for <c>HOST_VM_INFO64</c>, field for field as the operating
    /// system's own header declares it. Every field has to be here, in this order and at this width,
    /// even the ones nothing reads: the kernel writes the whole thing, and a field left out would put
    /// every field after it at the wrong place.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics64
    {
        public uint FreeCount;
        public uint ActiveCount;
        public uint InactiveCount;
        public uint WireCount;
        public ulong ZeroFillCount;
        public ulong Reactivations;
        public ulong PageIns;
        public ulong PageOuts;
        public ulong Faults;
        public ulong CopyOnWriteFaults;
        public ulong Lookups;
        public ulong Hits;
        public ulong Purges;
        public uint PurgeableCount;
        public uint SpeculativeCount;
        public ulong Decompressions;
        public ulong Compressions;
        public ulong SwapIns;
        public ulong SwapOuts;
        public uint CompressorPageCount;
        public uint ThrottledCount;
        public uint ExternalPageCount;
        public uint InternalPageCount;
        public ulong TotalUncompressedPagesInCompressor;
    }

    /// <summary>
    /// The shape the kernel fills in for <c>HOST_CPU_LOAD_INFO</c>: four tick counters, in the order
    /// the operating system's own header numbers them.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CpuLoadInfo
    {
        public uint User;
        public uint System;
        public uint Idle;
        public uint Nice;
    }

    [DllImport(LibSystem)]
    private static extern uint mach_host_self();

    [DllImport(LibSystem)]
    private static extern int host_page_size(uint hostPort, out IntPtr pageSize);

    [DllImport(LibSystem)]
    private static extern int host_statistics64(
        uint hostPort,
        int flavor,
        ref VmStatistics64 info,
        ref uint count);

    [DllImport(LibSystem)]
    private static extern int host_statistics(
        uint hostPort,
        int flavor,
        ref CpuLoadInfo info,
        ref uint count);

    [DllImport(LibSystem, SetLastError = true)]
    private static extern int sysctlbyname(
        byte[] name,
        ref long oldValue,
        ref IntPtr oldValueLength,
        IntPtr newValue,
        IntPtr newValueLength);
}
