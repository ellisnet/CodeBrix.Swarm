using System;
using System.IO;
using System.Reflection;

namespace CodeBrix.Swarm.EndToEnd.Tests.Infrastructure;

/// <summary>
/// The real sample Worker executable, and the little pieces of JSON that tell it what to do. These
/// scenarios start the actual program a consumer would write - not a stand-in for one - so everything
/// between a Hive's decision and a Worker's exit code is exercised as it really is.
/// </summary>
internal static class SampleWorkerProgram
{
    private const string MetadataKey = "SwarmSampleWorkerExecutable";

    private static readonly Lazy<string> Executable = new(Locate);

    /// <summary>
    /// Where the sample Worker was built. The project is referenced so that it is always built before
    /// this suite runs, but without its assembly, so nothing copies it beside these tests: the path is
    /// written into this assembly at build time and read back here.
    /// </summary>
    public static string Path => Executable.Value;

    /// <summary>
    /// Work that keeps a Worker busy until something ends it, saying nothing at all.
    /// </summary>
    public static SampleWork RunsUntilEnded() => new()
    {
        Label = "endless",
        Steps = 0,
        StepMilliseconds = 100
    };

    /// <summary>
    /// Work that keeps a Worker busy until something ends it, writing a line for everything that
    /// happens - including the configuration it was handed.
    /// </summary>
    public static SampleWork RunsUntilEndedAndReports() => new()
    {
        Label = "endless",
        Steps = 0,
        StepMilliseconds = 100,
        Report = true
    };

    /// <summary>
    /// Work that finishes by itself almost at once, so the Worker exits normally.
    /// </summary>
    public static SampleWork FinishesAtOnce() => new()
    {
        Label = "brief",
        Steps = 1,
        StepMilliseconds = 10
    };

    /// <summary>
    /// Work that fails before it has done anything, so the Worker exits with the code for work that
    /// threw. This is the ending a Hive waits after.
    /// </summary>
    public static SampleWork FailsAtOnce() => new()
    {
        Label = "hopeless",
        Steps = 1,
        StepMilliseconds = 0,
        FailAtStep = 0
    };

    /// <summary>
    /// The sample Worker's own switch that shortens how long it waits for a coordinator that is not
    /// answering. It belongs to the sample application, not to the library, which reads nothing from a
    /// command line but <c>--swarm-config</c>.
    /// </summary>
    /// <param name="seconds">How long to wait.</param>
    /// <returns>The argument to start the Worker with.</returns>
    public static string QueenWindowArgument(double seconds)
        => "--queen-window-seconds=" + seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Locate()
    {
        var assembly = typeof(SampleWorkerProgram).Assembly;

        foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (!string.Equals(metadata.Key, MetadataKey, StringComparison.Ordinal))
            {
                continue;
            }

            var path = metadata.Value;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                throw new InvalidOperationException(
                    "The sample Worker executable is not at '" + path + "'. It is referenced by this "
                    + "test project, so building the solution builds it; a missing one means the "
                    + "reference or the path this assembly carries has drifted.");
            }

            return path;
        }

        throw new InvalidOperationException(
            "This test assembly does not say where the sample Worker executable is. The test project "
            + "writes that in as assembly metadata named '" + MetadataKey + "'.");
    }
}
