using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Hive.HostLoad;

/// <summary>
/// Measures how busy the host is. There is one implementation per operating system, each of them
/// calling that system's own libraries, and a test puts a substitute here so that a suite can
/// describe a quiet host or a desperate one without having to arrange either.
/// </summary>
internal interface IHostLoadProbe
{
    /// <summary>
    /// Takes a reading. The processor figure is worked out from the difference since the last call,
    /// so the readings are expected to be taken one after another by the same probe.
    /// </summary>
    /// <param name="cancellationToken">Cancelled to give up on the reading.</param>
    /// <returns>
    /// The reading, or <see cref="HostLoadReading.Unknown" /> when the host could not be measured.
    /// </returns>
    Task<HostLoadReading> ReadAsync(CancellationToken cancellationToken);
}
