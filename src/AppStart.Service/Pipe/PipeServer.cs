using System.IO.Pipes;
using AppStart.Core.Protocol;
using AppStart.Service.Launch;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppStart.Service.Pipe;

/// <summary>
/// Hosts \\.\pipe\AppStart. Creates the first instance with FILE_FLAG_FIRST_PIPE_INSTANCE at start;
/// if that fails the service stops (spec pipe-security, "Gefälschte Pipe"). Serves up to
/// <see cref="PipeFactory.MaxInstances"/> clients in parallel, one message each.
/// </summary>
public sealed class PipeServer(LaunchRequestHandler handler, ILogger<PipeServer> log, IHostApplicationLifetime lifetime) : BackgroundService
{
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan GroupRetry = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string sddl;
        NamedPipeServerStream first;
        var waitingLogged = false;
        while (true)
        {
            try
            {
                // Before AppStart.Setup ran, the group "AppStart Users" does not exist yet. The service stays
                // running and retries, so it serves requests as soon as setup has created the group.
                sddl = PipeFactory.BuildSddl(PipeFactory.ResolveOperatorGroup());
            }
            catch (System.Security.Principal.IdentityNotMappedException)
            {
                if (!waitingLogged)
                {
                    log.LogWarning(EventIds.PipeSetupFailed,
                        "Gruppe «{Group}» fehlt: AppStart ist noch nicht eingerichtet. Der Dienst wartet und versucht es alle {Seconds} s erneut.",
                        Core.AppStartConstants.OperatorGroupName, GroupRetry.TotalSeconds);
                    waitingLogged = true;
                }

                try
                {
                    await Task.Delay(GroupRetry, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                continue;
            }

            break;
        }

        try
        {
            first = PipeFactory.Create(sddl, firstInstance: true);
        }
        catch (PipeAlreadyExistsException ex)
        {
            log.LogCritical(EventIds.PipeAlreadyExists, "Dienst startet nicht: {Message}", ex.Message);
            Environment.ExitCode = 2;
            lifetime.StopApplication();
            return;
        }
        catch (Exception ex)
        {
            log.LogCritical(EventIds.PipeSetupFailed, ex, "Dienst startet nicht: Pipe kann nicht angelegt werden. {Message}", ex.Message);
            Environment.ExitCode = 3;
            lifetime.StopApplication();
            return;
        }

        log.LogInformation(EventIds.ServiceStarted, "Dienst AppStart gestartet, Pipe bereit. Rechte: {Sddl}", sddl);

        var workers = new Task[PipeFactory.MaxInstances];
        workers[0] = ServeAsync(first, sddl, stoppingToken);
        for (var i = 1; i < workers.Length; i++)
        {
            workers[i] = ServeAsync(null, sddl, stoppingToken);
        }

        await Task.WhenAll(workers);
        log.LogInformation(EventIds.ServiceStopped, "Dienst AppStart beendet.");
    }

    // One worker per pipe instance. A fault in one connection is logged and must never stop the service:
    // otherwise a single misbehaving client could switch AppStart off for everyone.
    private async Task ServeAsync(NamedPipeServerStream? instance, string sddl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = instance ?? PipeFactory.Create(sddl, firstInstance: false);
            }
            catch (Exception ex)
            {
                log.LogError(EventIds.PipeError, ex, "Pipe-Instanz kann nicht angelegt werden, neuer Versuch in 5 s: {Message}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, TaskScheduler.Default);
                continue;
            }

            instance = null;
            await using (server)
            {
                try
                {
                    await server.WaitForConnectionAsync(ct);
                    await HandleClientAsync(server, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    log.LogWarning(EventIds.PipeError, ex, "Pipe-Verbindung abgebrochen: {Message}", ex.Message);
                }
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        var session = PipeFactory.GetClientSessionId(server);
        string user;
        try
        {
            user = server.GetImpersonationUserName();
        }
        catch (IOException)
        {
            user = "(unbekannt)";
        }

        // Read one message with timeout. One byte more than allowed, so an oversized message is detectable.
        var buffer = new byte[PipeProtocol.MaxMessageBytes + 1];
        int length;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            timeout.CancelAfter(ReadTimeout);
            try
            {
                length = await server.ReadAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(EventIds.InvalidRequest, "Keine Anfrage innerhalb von {Timeout} s. Benutzer: {User}, Session: {Session}.",
                    ReadTimeout.TotalSeconds, user, session);
                return;
            }
        }

        if (!server.IsMessageComplete)
        {
            length = buffer.Length; // > 64 bytes: rejected by the parser
        }

        var context = new RequestContext(session, user);
        var request = buffer.AsMemory(0, length);
        var response = await Task.Run(() => handler.Handle(request.Span, context), ct);
        if (response is null)
        {
            return; // malformed: drop without answer
        }

        await server.WriteAsync(response, ct);
        await server.FlushAsync(ct);

        // Give the client a moment to read before the instance is disconnected.
        using var drain = CancellationTokenSource.CreateLinkedTokenSource(ct);
        drain.CancelAfter(ReadTimeout);
        try
        {
            _ = await server.ReadAsync(new byte[1], drain.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
    }
}
