using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Hive.HostLoad;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// A host with plenty of room, whatever the machine running these scenarios is really doing.
/// </summary>
/// <remarks>
/// EVERYTHING ELSE IN THIS SUITE IS REAL - a real coordinator on a real port, a real Hive, real Worker
/// processes - but the host reading is substituted on purpose. These scenarios are about what a Hive
/// DOES with a reading, and a suite that failed because the machine it ran on happened to be busy would
/// say nothing about that. The real reading of a real host has its own checks, in the Hive's own suite,
/// where a machine's own kernel files are read and the arithmetic is examined figure by figure.
/// </remarks>
internal sealed class QuietHostProbe : IHostLoadProbe
{
    private const long Gibibyte = 1024L * 1024L * 1024L;

    private int _reads;

    /// <summary>How many readings have been taken.</summary>
    public int Reads => Volatile.Read(ref _reads);

    public Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Interlocked.Increment(ref _reads);

        //Sixty-four gibibytes, forty-eight of them available, and nothing much happening - so the only
        //thing that can hold a Hive back in these scenarios is a limit it was given.
        return Task.FromResult(new HostLoadReading(64L * Gibibyte, 48L * Gibibyte, 3d));
    }
}
