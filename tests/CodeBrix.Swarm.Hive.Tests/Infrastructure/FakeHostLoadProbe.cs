using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Hive.HostLoad;

namespace CodeBrix.Swarm.Hive.Tests.Infrastructure;

/// <summary>
/// A host that is whatever the test says it is. It counts the readings taken from it and lets a test do
/// something on each one, which is how a suite drives a Hive's loop to a particular point and then ends
/// it: under a clock that never sleeps the loop never pauses, so the only way in is a call it makes.
/// </summary>
internal sealed class FakeHostLoadProbe : IHostLoadProbe
{
    private readonly Func<int, HostLoadReading> _reading;

    private int _reads;

    public FakeHostLoadProbe()
        : this(QuietHost()) { }

    public FakeHostLoadProbe(HostLoadReading always)
        : this(_ => always) { }

    public FakeHostLoadProbe(Func<int, HostLoadReading> reading)
        => _reading = reading ?? (_ => QuietHost());

    /// <summary>Called with the number of this reading, counting from one.</summary>
    public Action<int> OnRead { get; set; }

    /// <summary>How many readings have been taken.</summary>
    public int Reads => Volatile.Read(ref _reads);

    /// <summary>
    /// A host with plenty of room: sixty-four gibibytes, forty-eight of them available, and nothing
    /// much happening.
    /// </summary>
    public static HostLoadReading QuietHost()
        => new(64L * 1024L * 1024L * 1024L, 48L * 1024L * 1024L * 1024L, 5d);

    /// <summary>
    /// A host with almost nothing left: sixty-four gibibytes, a hundred mebibytes available.
    /// </summary>
    public static HostLoadReading FullHost()
        => new(64L * 1024L * 1024L * 1024L, 100L * 1024L * 1024L, 5d);

    public Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var read = Interlocked.Increment(ref _reads);
        var reading = _reading(read);

        OnRead?.Invoke(read);

        return Task.FromResult(reading);
    }
}
