using System.Security.AccessControl;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;

namespace AppStart.Setup.Engine.Verification;

/// <summary>
/// Read-only system access of verify (services, pipe, files, registry, ACL reads). Tests replace it with a
/// fake; <see cref="SystemProbe"/> is the real implementation. Nothing here changes the system.
/// </summary>
public interface ISystemProbe
{
    /// <summary>Name of this computer (report "Rechner").</summary>
    string MachineName { get; }

    /// <summary>Windows user running verify, DOMAIN\User (report "Servicetechniker").</summary>
    string CurrentUser { get; }

    /// <summary>Public Start menu, Programs folder (C:\ProgramData\Microsoft\Windows\Start Menu\Programs).</summary>
    string CommonStartMenuPrograms { get; }

    /// <summary>Public desktop (C:\Users\Public\Desktop).</summary>
    string CommonDesktop { get; }

    /// <summary>State and configuration of a Windows service; <see cref="ServiceFacts.Exists"/> false when not installed.</summary>
    ServiceFacts GetService(string serviceName);

    /// <summary>Whether \\.\pipe\&lt;name&gt; exists and its security descriptor (opening it counts as a connection for the server).</summary>
    PipeFacts GetPipe(string pipeName);

    /// <summary>SID of a local or domain account or group; null when it cannot be resolved.</summary>
    SecurityIdentifier? TryResolveSid(string accountName);

    /// <summary>Name of a SID as DOMAIN\Name; null when it cannot be translated.</summary>
    string? TryTranslateSid(SecurityIdentifier sid);

    bool DirectoryExists(string path);

    bool FileExists(string path);

    /// <summary>Key under HKLM\ (64-bit view). An existing key that cannot be opened counts as existing.</summary>
    bool RegistryKeyExists(string key);

    /// <summary><see cref="AppStart.Core.Security.WriteAccessPolicy.FindViolations(string)"/>.</summary>
    IReadOnlyList<string> FindWriteViolations(string path);

    /// <summary><see cref="ConfigStore.Load"/>: content and ACLs of apps.json and its folder.</summary>
    ConfigLoadResult LoadConfig(string path);

    /// <summary>Parses apps.json without the ACL check, so the per-app checks still run when only the ACL is wrong.</summary>
    ConfigLoadResult ParseConfigWithoutAclCheck(string path);

    /// <summary>Opens the EXE locked (<see cref="LockedExecutable"/>) and runs <see cref="ExecutableVerifier.Verify"/>.</summary>
    IntegrityResult VerifyExecutable(AppEntry app);
}

/// <summary>
/// Facts of a service. <paramref name="State"/> and <paramref name="StartType"/> are the Win32 values
/// (SERVICE_RUNNING = 4, SERVICE_AUTO_START = 2); <paramref name="Account"/> is lpServiceStartName.
/// </summary>
public sealed record ServiceFacts(bool Exists, int State, int StartType, string? Account)
{
    public const int StateRunning = 4;
    public const int StartTypeAutomatic = 2;

    public static ServiceFacts Missing { get; } = new(false, 0, 0, null);
}

/// <summary>Facts of a named pipe. <paramref name="Error"/> is set when it exists but its descriptor cannot be read.</summary>
public sealed record PipeFacts(bool Exists, RawSecurityDescriptor? Descriptor, string? Error)
{
    public static PipeFacts Missing { get; } = new(false, null, null);
}
