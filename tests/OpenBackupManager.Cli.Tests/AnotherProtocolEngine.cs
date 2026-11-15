using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// Answers hello on a home's socket as an engine of another protocol does, until disposed
internal sealed class AnotherProtocolEngine : IDisposable
{
    private readonly string _name;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _answering;

    public AnotherProtocolEngine(string home)
    {
        _name = EngineSocket.Name(AppPaths.InHome(home));
        _answering = Task.Run(() => AnswerAsync(_stopping.Token));
    }

    public void Dispose()
    {
        _stopping.Cancel();
        try
        {
            _answering.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }

        _stopping.Dispose();
    }

    private async Task AnswerAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await using var pipe = new NamedPipeServerStream(
                _name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);
            await RefuseAsync(pipe, cancellationToken);
        }
    }

    private static async Task RefuseAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            if (await reader.ReadLineAsync(cancellationToken) is not { } line)
            {
                return;
            }

            using var hello = JsonDocument.Parse(line);
            var id = hello.RootElement.GetProperty("id").GetRawText();
            await pipe.WriteAsync(
                Encoding.UTF8.GetBytes($$$"""{"jsonrpc":"2.0","id":{{{id}}},"error":{"code":{{{RpcError.ProtocolMismatch}}},"message":"Another version"}}""" + "\n"),
                cancellationToken);
        }
        catch (IOException)
        {
        }
    }
}
