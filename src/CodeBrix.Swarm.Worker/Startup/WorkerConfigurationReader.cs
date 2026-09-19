using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Swarm.Core;
using CodeBrix.Swarm.Core.Configuration;

namespace CodeBrix.Swarm.Worker.Startup;

/// <summary>
/// Finds a Worker's configuration and reads it.
/// </summary>
/// <remarks>
/// <para>
/// Normally it is the first line of standard input, written there by the Hive that started this
/// process. It is never taken from the command line or from an environment variable, because both
/// of those are readable by other processes on the host and the access token is in it.
/// </para>
/// <para>
/// ONE SWITCH, AND ONLY THAT ONE, switches on development mode:
/// <c>--swarm-config &lt;path&gt;</c> or <c>--swarm-config=&lt;path&gt;</c>. The same JSON is then
/// read from that file, and the process runs without a lifeline, so a Worker can be started by hand
/// with no Hive anywhere.
/// </para>
/// <para>
/// EVERY OTHER ARGUMENT IS LEFT ALONE. The command line belongs to the consuming application: it
/// may define whatever switches and positional arguments it likes, and none of them can put a
/// Worker into development mode by accident. The switch is named with the library's own prefix for
/// the same reason - so that an application's own <c>--config</c> cannot collide with it.
/// </para>
/// </remarks>
internal static class WorkerConfigurationReader
{
    /// <summary>
    /// The one switch that names a configuration file and puts the Worker into development mode. It
    /// takes the path either as the next argument or after an equals sign.
    /// </summary>
    public const string ConfigurationSwitch = "--swarm-config";

    /// <summary>
    /// Reads the configuration, from the file named with <see cref="ConfigurationSwitch" /> when
    /// there is one and from standard input otherwise.
    /// </summary>
    /// <param name="args">The process's command-line arguments, without the program name.</param>
    /// <param name="standardInput">Where the line of JSON arrives when there is no file.</param>
    /// <param name="cancellationToken">Cancelled to give up on waiting for the line.</param>
    /// <returns>The configuration and where it came from.</returns>
    /// <exception cref="SwarmConfigurationException">
    /// Nothing arrived, the file could not be read, the text was not JSON, or something the swarm
    /// needs was missing.
    /// </exception>
    public static async Task<WorkerStartup> ReadAsync(
        string[] args,
        TextReader standardInput,
        CancellationToken cancellationToken)
    {
        var path = FindConfigurationPath(args);

        var json = path == null
            ? await ReadFirstLineAsync(standardInput, cancellationToken).ConfigureAwait(false)
            : await ReadFileAsync(path, cancellationToken).ConfigureAwait(false);

        var configuration = WorkerConfiguration.Parse(json);
        configuration.Validate();

        return new WorkerStartup(configuration, path);
    }

    /// <summary>
    /// Picks the configuration file out of the command line: the value after
    /// <see cref="ConfigurationSwitch" />, or the value attached to it after an equals sign. Nothing
    /// else on the command line is looked at - there is no positional argument that means a
    /// configuration file, and no other switch name is recognised.
    /// </summary>
    /// <param name="args">The process's command-line arguments, without the program name.</param>
    /// <returns>The path, or null when the switch was not given.</returns>
    /// <exception cref="SwarmConfigurationException">
    /// The switch was given with nothing after it, or with an empty value after the equals sign.
    /// </exception>
    public static string FindConfigurationPath(string[] args)
    {
        if (args == null)
        {
            return null;
        }

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (string.IsNullOrWhiteSpace(argument))
            {
                continue;
            }

            argument = argument.Trim();

            if (argument.Equals(ConfigurationSwitch, StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    return args[index + 1].Trim();
                }

                throw new SwarmConfigurationException(
                    $"'{ConfigurationSwitch}' was given with no file after it.");
            }

            if (argument.Length > ConfigurationSwitch.Length
                && argument[ConfigurationSwitch.Length] == '='
                && argument.AsSpan(0, ConfigurationSwitch.Length)
                    .Equals(ConfigurationSwitch, StringComparison.OrdinalIgnoreCase))
            {
                var attached = argument[(ConfigurationSwitch.Length + 1)..].Trim();

                if (attached.Length > 0)
                {
                    return attached;
                }

                throw new SwarmConfigurationException(
                    $"'{ConfigurationSwitch}=' was given with no file after the equals sign.");
            }
        }

        return null;
    }

    private static async Task<string> ReadFirstLineAsync(
        TextReader standardInput,
        CancellationToken cancellationToken)
    {
        if (standardInput == null)
        {
            throw new SwarmConfigurationException(
                "This Worker has no standard input to read its configuration from, and no "
                + "configuration file was named on the command line.");
        }

        string line;

        try
        {
            line = await standardInput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SwarmConfigurationException(
                "The Worker's configuration could not be read from standard input.", ex);
        }

        if (string.IsNullOrWhiteSpace(line))
        {
            throw new SwarmConfigurationException(
                "Standard input ended before the Worker's configuration arrived. A Hive writes one "
                + "line of JSON and then leaves the pipe open; to start a Worker by hand, put the "
                + $"same JSON in a file and name it with '{ConfigurationSwitch} <path>'.");
        }

        return line;
    }

    private static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SwarmConfigurationException(
                $"The Worker's configuration file could not be read: '{path}'.", ex);
        }
    }
}
