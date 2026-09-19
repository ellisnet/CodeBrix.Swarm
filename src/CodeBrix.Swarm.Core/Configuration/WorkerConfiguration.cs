using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeBrix.Swarm.Core.Messaging;

namespace CodeBrix.Swarm.Core.Configuration;

/// <summary>
/// Everything a Worker is told when it starts, as one line of JSON. The Hive writes it to the
/// Worker's standard input and then leaves the pipe open as a lifeline; a Worker started by hand
/// reads the same JSON from a file named on its command line.
/// </summary>
/// <remarks>
/// It never travels on the command line or in an environment variable, because both of those are
/// readable by other processes on the host and the access token is in here.
/// </remarks>
public sealed class WorkerConfiguration
{
    /// <summary>
    /// The swarm's own part: the coordinator's address, the access token and the Worker's name.
    /// </summary>
    [JsonPropertyName("swarm")]
    public WorkerSwarmSettings Swarm { get; set; }

    /// <summary>
    /// The consuming application's own part, as JSON text. The swarm carries it from the Hive to the
    /// Worker without reading it, and hands it to the application's work exactly as it arrived. Any
    /// JSON value is allowed - an object, an array, a number - and null means the application sent
    /// nothing.
    /// </summary>
    [JsonPropertyName("work")]
    [JsonConverter(typeof(RawJsonConverter))]
    public string Work { get; set; }

    /// <summary>
    /// Writes the configuration as the single line of JSON a Worker expects, with no line break at
    /// the end.
    /// </summary>
    /// <returns>The JSON text.</returns>
    public string ToJsonLine() => JsonSerializer.Serialize(this, SwarmJson.Options);

    /// <summary>
    /// Reads a configuration back from the line of JSON a Worker was given.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The configuration. It is not checked for completeness - call <see cref="Validate" />.</returns>
    /// <exception cref="SwarmConfigurationException">
    /// The text is empty, is not valid JSON, or is JSON null.
    /// </exception>
    public static WorkerConfiguration Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new SwarmConfigurationException(
                "The Worker configuration was empty. It is one line of JSON, read from standard input "
                + "or from the file named on the command line.");
        }

        WorkerConfiguration configuration;

        try
        {
            configuration = JsonSerializer.Deserialize<WorkerConfiguration>(json, SwarmJson.Options);
        }
        catch (JsonException ex)
        {
            throw new SwarmConfigurationException(
                "The Worker configuration was not valid JSON: " + ex.Message, ex);
        }

        if (configuration == null)
        {
            throw new SwarmConfigurationException("The Worker configuration was JSON null.");
        }

        return configuration;
    }

    /// <summary>
    /// Checks that everything the swarm needs is present.
    /// </summary>
    /// <exception cref="SwarmConfigurationException">
    /// The swarm part is missing, one of its values is missing or whitespace, or the coordinator's
    /// address is not an absolute http or https address.
    /// </exception>
    public void Validate()
    {
        if (Swarm == null)
        {
            throw new SwarmConfigurationException(
                "The Worker configuration has no 'swarm' part, so the Worker does not know where the "
                + "coordinator is or how to be let in.");
        }

        RequireValue(Swarm.QueenUrl, "swarm.queenUrl", "the coordinator's base address");
        RequireValue(Swarm.Token, "swarm.token", "the Worker's access token");
        RequireValue(Swarm.WorkerId, "swarm.workerId", "the name this Worker is known by");

        //AN ABSOLUTE ADDRESS IS NOT ENOUGH. On Linux a path such as "/swarm/worker" parses perfectly
        //well as an absolute file address. Refusing it here is what turns an address a Worker could
        //never connect to into the documented exit code for a configuration it cannot use, rather than
        //a failure thrown out of the Worker's own start-up.
        if (!Uri.TryCreate(Swarm.QueenUrl.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new SwarmConfigurationException(
                $"The Worker configuration's 'swarm.queenUrl' must be an absolute http or https "
                + $"address, and was '{Swarm.QueenUrl}'.");
        }
    }

    private static void RequireValue(string value, string field, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SwarmConfigurationException(
                $"The Worker configuration is missing '{field}' - {what}.");
        }
    }
}
