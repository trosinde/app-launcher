using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

// AppStart TestHelper: a helper program of the TestApp. When it runs, the start was allowed.
// It also tries to start a grandchild (cmd.exe /c exit 7) and reports what happened, for the spec scenario
// «Enkelprozess» (add-allowed-child-processes): in mode allowlist AppStart must terminate it.
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"AppStart TestHelper läuft als {identity.Name}, Admin-Rechte: {(elevated ? "ja" : "nein")}. Enkelprozess cmd.exe: {Grandchild()}.");
return 0;

static string Grandchild()
{
    try
    {
        using var cmd = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 7") { UseShellExecute = false, CreateNoWindow = true })!;
        if (!cmd.WaitForExit(5000))
        {
            return "läuft noch";
        }

        return cmd.ExitCode switch
        {
            7 => "gestartet",
            0xC0DE => "von AppStart beendet",
            var code => $"beendet mit {code}",
        };
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 1816)
    {
        return "blockiert durch Job-Objekt";
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
    {
        return "von AppStart beendet";
    }
    catch (Win32Exception ex)
    {
        return "Fehler " + ex.NativeErrorCode;
    }
}
