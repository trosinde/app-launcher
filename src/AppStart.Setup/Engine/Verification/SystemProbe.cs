using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Security;
using Microsoft.Win32;

namespace AppStart.Setup.Engine.Verification;

/// <summary>Real, read-only implementation of <see cref="ISystemProbe"/>.</summary>
public sealed partial class SystemProbe : ISystemProbe
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceQueryConfig = 0x0001;
    private const uint ServiceQueryStatus = 0x0004;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorFileNotFound = 2;
    private const int SeFileObject = 1;
    private const uint DaclSecurityInformation = 0x4;

    public string MachineName => Environment.MachineName;

    public string CurrentUser
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.Name;
        }
    }

    public string CommonStartMenuPrograms => Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

    public string CommonDesktop => Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    public ServiceFacts GetService(string serviceName)
    {
        var manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "OpenSCManager");
        }

        try
        {
            var service = OpenService(manager, serviceName, ServiceQueryConfig | ServiceQueryStatus);
            if (service == 0)
            {
                var error = Marshal.GetLastPInvokeError();
                return error == ErrorServiceDoesNotExist ? ServiceFacts.Missing : throw new Win32Exception(error, "OpenService");
            }

            try
            {
                if (!QueryServiceStatus(service, out var status))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "QueryServiceStatus");
                }

                var (startType, account) = QueryConfig(service);
                return new ServiceFacts(true, (int)status.CurrentState, startType, account);
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    private static unsafe (int StartType, string? Account) QueryConfig(nint service)
    {
        QueryServiceConfig(service, 0, 0, out var needed);
        var error = Marshal.GetLastPInvokeError();
        if (error != ErrorInsufficientBuffer)
        {
            throw new Win32Exception(error, "QueryServiceConfig");
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!QueryServiceConfig(service, buffer, needed, out _))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "QueryServiceConfig");
            }

            var config = (QueryServiceConfigW*)buffer;
            return ((int)config->StartType, Marshal.PtrToStringUni(config->ServiceStartName));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public unsafe PipeFacts GetPipe(string pipeName)
    {
        if (!PipeListed(pipeName))
        {
            return PipeFacts.Missing;
        }

        nint descriptor;
        var result = GetNamedSecurityInfo(@"\\.\pipe\" + pipeName, SeFileObject, DaclSecurityInformation, 0, 0, 0, 0, &descriptor);
        if (result != 0)
        {
            return result == ErrorFileNotFound
                ? PipeFacts.Missing
                : new PipeFacts(true, null, new Win32Exception((int)result).Message);
        }

        try
        {
            var length = (int)GetSecurityDescriptorLength(descriptor);
            var bytes = new byte[length];
            Marshal.Copy(descriptor, bytes, 0, length);
            return new PipeFacts(true, new RawSecurityDescriptor(bytes, 0), null);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    // Listing \\.\pipe\ does not connect to the pipe; only reading the descriptor does.
    private static bool PipeListed(string pipeName)
    {
        try
        {
            return Directory.GetFiles(@"\\.\pipe\")
                .Any(p => string.Equals(Path.GetFileName(p), pipeName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return true; // let GetNamedSecurityInfo decide
        }
    }

    public SecurityIdentifier? TryResolveSid(string accountName)
    {
        try
        {
            return (SecurityIdentifier)new NTAccount(accountName).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    public string? TryTranslateSid(SecurityIdentifier sid)
    {
        try
        {
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return null;
        }
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool RegistryKeyExists(string key)
    {
        const string prefix = @"HKLM\";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        try
        {
            using var sub = root.OpenSubKey(key[prefix.Length..], writable: false);
            return sub is not null;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            return true; // exists, but locked (expected for a protected key when not elevated)
        }
    }

    public IReadOnlyList<string> FindWriteViolations(string path) => WriteAccessPolicy.FindViolations(path);

    public ConfigLoadResult LoadConfig(string path) => ConfigStore.Load(path);

    public ConfigLoadResult ParseConfigWithoutAclCheck(string path)
    {
        try
        {
            return AppsConfigSerializer.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigLoadResult.Fail(ex.Message);
        }
    }

    public IntegrityResult VerifyChild(AllowedChild child)
    {
        try
        {
            using var exe = LockedExecutable.Open(child.Exe);
            return ExecutableVerifier.Verify(child, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IntegrityResult.Fail(ex.Message);
        }
    }

    public AppStart.Core.Learning.LearnStateLoadResult LoadLearnState(string path) => AppStart.Core.Learning.LearnStateStore.Load(path);

    public IntegrityResult VerifyExecutable(AppEntry app)
    {
        try
        {
            using var exe = LockedExecutable.Open(app.Exe);
            return ExecutableVerifier.Verify(app, exe);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return IntegrityResult.Fail(VerificationTexts.ExeMissing);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return IntegrityResult.Fail(string.Format(VerificationTexts.ExeNotReadable, ex.Message));
        }
    }

    // ------------------------------------------------------------------ Win32 (own declarations: the Setup project's
    // CsWin32 NativeMethods.txt is outside this folder)

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryServiceConfigW
    {
        public uint ServiceType;
        public uint StartType;
        public uint ErrorControl;
        public nint BinaryPathName;
        public nint LoadOrderGroup;
        public uint TagId;
        public nint Dependencies;
        public nint ServiceStartName;
        public nint DisplayName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint OpenService(nint manager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceStatus(nint service, out ServiceStatus status);

    [LibraryImport("advapi32.dll", EntryPoint = "QueryServiceConfigW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryServiceConfig(nint service, nint config, uint bufferSize, out uint bytesNeeded);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseServiceHandle(nint handle);

    [LibraryImport("advapi32.dll", EntryPoint = "GetNamedSecurityInfoW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial uint GetNamedSecurityInfo(
        string objectName, int objectType, uint securityInfo, nint owner, nint group, nint dacl, nint sacl, nint* descriptor);

    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityDescriptorLength(nint descriptor);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
