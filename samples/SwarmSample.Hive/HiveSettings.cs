using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SwarmSample.Hive;

/// <summary>
/// What this sample Hive needs to know, as one line of JSON.
/// </summary>
/// <remarks>
/// <para>
/// IT ARRIVES THE SAME WAY A WORKER'S CONFIGURATION DOES - on standard input, or from a file named on
/// the command line - and for the same reason: it carries two access tokens, and both a command line
/// and an environment variable can be read by anything else running on the host. A real application
/// would get them from wherever it keeps its secrets; what matters is that they do not travel
/// anywhere that leaves them lying about.
/// </para>
/// <para>
/// The sample Queen prints a line of exactly this shape, ready to be handed to this program.
/// </para>
/// </remarks>
internal sealed class HiveSettings
{
    /// <summary>The switch that names a file to read these from instead of standard input.</summary>
    public const string FileSwitch = "--settings=";

    /// <summary>The coordinator's base address.</summary>
    [JsonPropertyName("queenUrl")]
    public string QueenUrl { get; set; }

    /// <summary>This Hive's own access token, good only at the coordinator's Hive hub.</summary>
    [JsonPropertyName("hiveToken")]
    public string HiveToken { get; set; }

    /// <summary>
    /// The access token to pass on to every Worker, good only at the coordinator's Worker hub.
    /// </summary>
    [JsonPropertyName("workerToken")]
    public string WorkerToken { get; set; }

    /// <summary>The program to start as a Worker.</summary>
    [JsonPropertyName("workerExecutable")]
    public string WorkerExecutable { get; set; }

    /// <summary>What to call this Hive, or null for the host's own name.</summary>
    [JsonPropertyName("hiveId")]
    public string HiveId { get; set; }

    /// <summary>
    /// The most Workers to run at once, or null for as many as the host has room for.
    /// </summary>
    [JsonPropertyName("maxWorkers")]
    public int? MaxWorkers { get; set; }

    /// <summary>
    /// How many Workers to hand out altogether before answering "not now" from then on, or zero to
    /// keep handing them out for as long as the Hive runs.
    /// </summary>
    [JsonPropertyName("totalWorkers")]
    public int TotalWorkers { get; set; }

    /// <summary>How many steps each Worker takes, or zero for until it is ended.</summary>
    [JsonPropertyName("workerSteps")]
    public int WorkerSteps { get; set; }

    /// <summary>How long each Worker waits between its steps.</summary>
    [JsonPropertyName("workerStepMilliseconds")]
    public int WorkerStepMilliseconds { get; set; } = 250;

    /// <summary>True to write a line for everything that happens, in the Hive and in its Workers.</summary>
    [JsonPropertyName("report")]
    public bool Report { get; set; } = true;

    /// <summary>
    /// Reads the settings from the file named on the command line, or from the first line of standard
    /// input.
    /// </summary>
    /// <param name="args">The command-line arguments, without the program name.</param>
    /// <param name="cancellationToken">Cancelled to give up on waiting for the line.</param>
    /// <returns>The settings, or null when nothing usable arrived.</returns>
    public static async Task<HiveSettings> ReadAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var json = await ReadJsonAsync(args, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonSerializer.Deserialize<HiveSettings>(json);
    }

    /// <summary>
    /// What is wrong with these settings, or null when nothing is.
    /// </summary>
    /// <returns>A message to show, or null.</returns>
    public string FirstProblem()
    {
        if (string.IsNullOrWhiteSpace(QueenUrl))
        {
            return "'queenUrl' is missing.";
        }

        if (string.IsNullOrWhiteSpace(HiveToken))
        {
            return "'hiveToken' is missing. The coordinator mints it.";
        }

        if (string.IsNullOrWhiteSpace(WorkerToken))
        {
            return "'workerToken' is missing. The coordinator mints it, and this Hive passes it on to "
                   + "every Worker it starts.";
        }

        if (string.IsNullOrWhiteSpace(WorkerExecutable))
        {
            return "'workerExecutable' is missing: there is nothing to start.";
        }

        if (!File.Exists(WorkerExecutable))
        {
            return "'workerExecutable' does not name a file that exists: '" + WorkerExecutable + "'.";
        }

        return null;
    }

    /// <summary>
    /// The arguments to start each Worker with. The configuration is never among them.
    /// </summary>
    /// <returns>The arguments, in order.</returns>
    public IReadOnlyList<string> WorkerArguments() => [];

    private static async Task<string> ReadJsonAsync(string[] args, CancellationToken cancellationToken)
    {
        var path = FindFile(args);

        if (path != null)
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        return await Console.In.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FindFile(string[] args)
    {
        if (args == null)
        {
            return null;
        }

        foreach (var argument in args)
        {
            if (argument != null && argument.StartsWith(FileSwitch, StringComparison.Ordinal))
            {
                var path = argument[FileSwitch.Length..].Trim();

                if (path.Length > 0)
                {
                    return path;
                }
            }
        }

        return null;
    }
}
