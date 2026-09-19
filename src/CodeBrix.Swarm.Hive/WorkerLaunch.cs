using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Hive;

/// <summary>
/// The consuming application's answer when a Hive has room and asks what to start next: either a
/// Worker to start, or <see cref="NotNow" />.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotNow" /> is never a failure and never counts against anything. A Hive that is told
/// "not now" simply waits and asks again, for as long as it is running: an application with nothing
/// to hand out yet says so as often as it likes.
/// </para>
/// <para>
/// The work the launch carries is the application's own and the swarm never reads it. It reaches the
/// Worker exactly as it was written, over the Worker's standard input, along with the swarm's own
/// part of the configuration.
/// </para>
/// </remarks>
public sealed class WorkerLaunch
{
    private static readonly string[] NoArguments = [];

    private static readonly WorkerLaunch Nothing = new();

    private WorkerLaunch() => Arguments = NoArguments;

    private WorkerLaunch(string executable, IEnumerable<string> arguments, string work)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException(
                "A Worker cannot be started without an executable to start.", nameof(executable));
        }

        Executable = executable.Trim();
        Work = work;

        Arguments = arguments == null
            ? NoArguments
            : arguments.Where(argument => argument != null).ToArray();
    }

    /// <summary>
    /// Nothing to start at the moment. The Hive waits and asks again; this is not a failure and the
    /// Hive does not wait any longer the next time because of it.
    /// </summary>
    public static WorkerLaunch NotNow => Nothing;

    /// <summary>
    /// True when this is <see cref="NotNow" /> rather than something to start.
    /// </summary>
    public bool IsNotNow => Executable == null;

    /// <summary>
    /// The program to start. Null when this is <see cref="NotNow" />.
    /// </summary>
    public string Executable { get; }

    /// <summary>
    /// The arguments to start it with, each one its own element - they are handed to the operating
    /// system separately, so nothing has to be quoted. Never null; empty when there are none.
    /// </summary>
    /// <remarks>
    /// The Worker's configuration is NOT among them. It carries an access token, and a command line
    /// is readable by anything else running on the host.
    /// </remarks>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// The application's own part of the Worker's configuration, as JSON text, or null when the
    /// application sends nothing. The swarm carries it to the Worker without reading it.
    /// </summary>
    public string Work { get; }

    /// <summary>
    /// The folder to start the program in, or null to start it in the Hive's own folder.
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Start a Worker with no arguments and nothing of the application's own in its configuration.
    /// </summary>
    /// <param name="executable">The program to start.</param>
    /// <returns>The launch to hand back to the Hive.</returns>
    /// <exception cref="ArgumentException">The executable is null, empty or whitespace.</exception>
    public static WorkerLaunch Start(string executable) => new(executable, null, null);

    /// <summary>
    /// Start a Worker with arguments and nothing of the application's own in its configuration.
    /// </summary>
    /// <param name="executable">The program to start.</param>
    /// <param name="arguments">The arguments, each one its own element.</param>
    /// <returns>The launch to hand back to the Hive.</returns>
    /// <exception cref="ArgumentException">The executable is null, empty or whitespace.</exception>
    public static WorkerLaunch Start(string executable, IEnumerable<string> arguments)
        => new(executable, arguments, null);

    /// <summary>
    /// Start a Worker with arguments and a piece of the application's own JSON to go in its
    /// configuration.
    /// </summary>
    /// <param name="executable">The program to start.</param>
    /// <param name="arguments">The arguments, each one its own element.</param>
    /// <param name="workJson">
    /// The application's part of the configuration, already written as JSON. It is copied through
    /// unread, so it may be any JSON value at all.
    /// </param>
    /// <returns>The launch to hand back to the Hive.</returns>
    /// <exception cref="ArgumentException">The executable is null, empty or whitespace.</exception>
    public static WorkerLaunch Start(string executable, IEnumerable<string> arguments, string workJson)
        => new(executable, arguments, workJson);

    /// <summary>
    /// Start a Worker with arguments and a value of the application's own, written to JSON for it.
    /// </summary>
    /// <typeparam name="TWork">The application's own type. The Worker reads it back with the same one.</typeparam>
    /// <param name="executable">The program to start.</param>
    /// <param name="arguments">The arguments, each one its own element.</param>
    /// <param name="work">The value to send. A null value sends nothing.</param>
    /// <returns>The launch to hand back to the Hive.</returns>
    /// <exception cref="ArgumentException">The executable is null, empty or whitespace.</exception>
    /// <exception cref="JsonException">The value could not be written to JSON.</exception>
    public static WorkerLaunch StartWithWork<TWork>(
        string executable,
        IEnumerable<string> arguments,
        TWork work)
        => new(
            executable,
            arguments,
            work == null ? null : JsonSerializer.Serialize(work, SwarmJson.Options));
}
