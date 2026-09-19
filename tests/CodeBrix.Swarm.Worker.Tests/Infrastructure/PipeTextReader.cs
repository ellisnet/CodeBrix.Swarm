using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CodeBrix.Swarm.Worker.Tests.Infrastructure;

/// <summary>
/// Stands in for the pipe a Hive writes a Worker's configuration to: lines can be pushed into it,
/// reading waits when there is nothing to read, and the pipe can be brought to an end on demand -
/// which is what a Worker sees when its Hive has gone.
/// </summary>
internal sealed class PipeTextReader : TextReader
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>();

    public void WriteLine(string line) => _lines.Writer.TryWrite(line);

    public void EndOfPipe() => _lines.Writer.TryComplete();

    public override async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _lines.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    public override string ReadLine()
        => throw new NotSupportedException("This reader is only read asynchronously.");
}
