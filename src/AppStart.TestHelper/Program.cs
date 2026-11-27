using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

// AppStart TestHelper: a helper program of the TestApp. When it runs, the start was allowed.
// It also tries to start a grandchild (cmd.exe /c exit 7) and reports what happened, for the spec scenario
// "Grandchild process" (add-allowed-child-processes): in mode allowlist AppStart must terminate it.
Console.OutputEncoding = System.Text.Encoding.UTF8; // read by the TestApp as UTF-8, not the OEM code page
using var identity = WindowsIdentity.GetCurrent();
var elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
Console.WriteLine($"AppStart TestHelper running as {identity.Name}, admin rights: {(elevated ? "yes" : "no")}. Grandchild process cmd.exe: {Grandchild()}.");
return 0;

static string Grandchild()
{
    try
    {
        using var cmd = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 7") { UseShellExecute = false, CreateNoWindow = true })!;
        if (!cmd.WaitForExit(5000))
        {
            return "still running";
        }

        return cmd.ExitCode switch
        {
            7 => "started",
            0xC0DE => "ended by AppStart",
            var code => $"ended with {code}",
        };
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode == 1816)
    {
        return "blocked by job object";
    }
    catch (Win32Exception ex) when (ex.NativeErrorCode is 5 or 299) // terminated during CreateProcess
    {
        return "ended by AppStart";
    }
    catch (Win32Exception ex)
    {
        return "error " + ex.NativeErrorCode;
    }
}
