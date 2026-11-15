using System.IO.Pipes;
using System.Security.Principal;
using AppStart.Core;
using AppStart.Core.Protocol;

namespace AppStart.Launcher;

/// <summary>Sends one START request to \\.\pipe\AppStart and reads the answer.</summary>
internal static class PipeClient
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The service needs a few seconds for S4U logon and profile load.</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(30);

    public static ClientResult SendStart(string appId)
    {
        // Identification only: the service may read who we are, but cannot act as us.
        using var pipe = new NamedPipeClientStream(".", AppStartConstants.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            pipe.Connect((int)ConnectTimeout.TotalMilliseconds);
        }
        catch (UnauthorizedAccessException)
        {
            return new ClientResult(ClientStatus.AccessDenied);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return new ClientResult(ClientStatus.Unreachable);
        }

        try
        {
            pipe.ReadMode = PipeTransmissionMode.Message;
            var request = PipeProtocol.FormatRequest(appId);
            pipe.Write(request);
            pipe.Flush();

            var buffer = new byte[PipeProtocol.MaxMessageBytes];
            var read = pipe.ReadAsync(buffer).AsTask();
            if (!read.Wait(AnswerTimeout) || read.Result == 0)
            {
                return new ClientResult(ClientStatus.ProtocolError);
            }

            return PipeProtocol.TryParseResponse(buffer.AsSpan(0, read.Result), out var response)
                ? new ClientResult(ClientStatus.Answered, response)
                : new ClientResult(ClientStatus.ProtocolError);
        }
        catch (Exception ex) when (ex is IOException or AggregateException or InvalidOperationException)
        {
            return new ClientResult(ClientStatus.ProtocolError);
        }
    }
}
