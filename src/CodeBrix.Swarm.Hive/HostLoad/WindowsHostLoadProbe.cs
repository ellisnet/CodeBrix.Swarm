using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Measures a Windows host through two of the operating system's own calls: one for memory and one
/// for the processor. Nothing is installed and nothing is built: both live in the library every
/// Windows process already has loaded.
/// </summary>
/// <remarks>
/// <para>
/// <c>GlobalMemoryStatusEx</c> reports the host's physical memory and how much of it is available.
/// Unlike the Linux figure this one needs no interpretation: Windows already answers the question
/// "how much could be handed out".
/// </para>
/// <para>
/// <c>GetSystemTimes</c> reports running totals, in hundreds of nanoseconds, of the time every
/// processor together has spent idle, in the kernel and in user code. THE KERNEL FIGURE INCLUDES THE
/// IDLE FIGURE - that is the part of this call that is easy to get wrong - so the time altogether is
/// kernel plus user, and the busy part of it is that total less idle.
/// </para>
/// <para>
/// The arithmetic is in <see cref="WindowsCpuTimes" />, where it is checked without Windows
/// underneath it.
/// </para>
/// </remarks>
internal sealed class WindowsHostLoadProbe : IHostLoadProbe
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _havePreviousTimes;

    /// <inheritdoc />
    public Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Read());
    }

    private HostLoadReading Read()
    {
        long totalBytes;
        long availableBytes;

        try
        {
            var status = new MemoryStatusEx
            {
                Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
            };

            if (!GlobalMemoryStatusEx(ref status))
            {
                return HostLoadReading.Unknown;
            }

            totalBytes = ToLong(status.TotalPhysical);
            availableBytes = ToLong(status.AvailablePhysical);
        }
        catch (Exception)
        {
            return HostLoadReading.Unknown;
        }

        if (totalBytes <= 0L)
        {
            return HostLoadReading.Unknown;
        }

        if (availableBytes > totalBytes)
        {
            availableBytes = totalBytes;
        }

        var busyPercent = 0d;

        try
        {
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                var idle = idleTime.ToUInt64();
                var kernel = kernelTime.ToUInt64();
                var user = userTime.ToUInt64();

                if (_havePreviousTimes)
                {
                    busyPercent = WindowsCpuTimes.BusyPercent(
                        _previousIdle, _previousKernel, _previousUser, idle, kernel, user);
                }

                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _havePreviousTimes = true;
            }
        }
        catch (Exception)
        {
            //The memory figures are the ones that decide whether a Worker starts; a processor
            //reading that could not be taken counts as nothing rather than stopping the reading.
            busyPercent = 0d;
        }

        return new HostLoadReading(totalBytes, availableBytes, busyPercent);
    }

    private static long ToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    /// <summary>
    /// The shape Windows expects for <c>GlobalMemoryStatusEx</c>. The first field has to be set to
    /// the size of the whole thing before the call, which is how Windows tells versions of the shape
    /// apart, and the rest are filled in by the call.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    /// <summary>
    /// Windows reports times as two 32-bit halves of one 64-bit count of hundreds of nanoseconds.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;

        public ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);
}
