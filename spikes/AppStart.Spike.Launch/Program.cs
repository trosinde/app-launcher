using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using static AppStart.Spike.Launch.Native;

namespace AppStart.Spike.Launch;

// Spike for tasks 1.1 and 1.2. Modes:
//   launch        Runs as SYSTEM (scheduled task). Gets a token for the app account (S4U or password),
//                 grants it access to WinSta0\Default of the target session and starts an EXE there.
//   grant-desktop Internal helper. Started by "launch" as SYSTEM inside the target session, because
//                 WinSta0 of another session cannot be opened from session 0.
//   probe         Started as the app account. Shows identity, elevation, HKCU and tries to start cmd.exe.
internal static unsafe class Program
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AppStartSpike");
    private static StreamWriter? _log;

    private static int Main(string[] args)
    {
        Directory.CreateDirectory(LogDir);
        var mode = args.Length > 0 ? args[0] : "help";
        _log = new StreamWriter(Path.Combine(LogDir, $"{mode}-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log")) { AutoFlush = true };
        try
        {
            return mode switch
            {
                "launch" => Launch(Options.Parse(args.Skip(1))),
                "grant-desktop" => GrantDesktop(Options.Parse(args.Skip(1))),
                "probe" => Probe(),
                _ => Help(),
            };
        }
        catch (Exception ex)
        {
            Log($"FATAL {ex}");
            return 99;
        }
    }

    private static int Help()
    {
        Log("""
            AppStart.Spike.Launch launch --user appstart-app --exe <path> [--args "<args>"]
                [--session <id>] [--logon batch|network|interactive|service] [--password-env <VAR>]
                [--no-desktop-acl] [--job] [--no-profile] [--wait]
            AppStart.Spike.Launch probe
            """);
        return 1;
    }

    // ------------------------------------------------------------------ launch (SYSTEM)
    private static int Launch(Options o)
    {
        Log($"Running as {WindowsIdentity.GetCurrent().Name}, session {Process.GetCurrentProcess().SessionId}");
        EnablePrivilege("SeTcbPrivilege");
        EnablePrivilege("SeAssignPrimaryTokenPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");

        var user = o.Get("user", "appstart-app");
        var exe = o.Require("exe");
        var session = o.Has("session") ? uint.Parse(o.Get("session")) : WTSGetActiveConsoleSessionId();
        var logonType = Enum.Parse<SECURITY_LOGON_TYPE>(o.Get("logon", "batch"), ignoreCase: true);
        Log($"Target session {session}, account {user}, logon type {logonType}");

        // --- 1.1: token for the app account
        IntPtr token;
        var pwVar = o.Get("password-env", "");
        if (pwVar.Length > 0)
        {
            var pw = Environment.GetEnvironmentVariable(pwVar) ?? throw new InvalidOperationException($"env {pwVar} not set");
            if (!LogonUserW(user, ".", pw, (int)logonType, 0, out token)) throw Win32("LogonUserW");
            Log("RESULT 1.1 LogonUser with password: OK");
        }
        else
        {
            token = S4ULogon(user, Environment.MachineName, logonType);
            Log("RESULT 1.1 S4U logon: OK");
        }
        DescribeToken("logon token", token);

        // UAC: an admin account may get a filtered token; SYSTEM (SeTcb) can obtain the linked full token.
        if (GetTokenUInt(token, TOKEN_INFORMATION_CLASS.TokenElevationType) == 3 /* Limited */)
        {
            var linked = GetTokenHandle(token, TOKEN_INFORMATION_CLASS.TokenLinkedToken);
            Log("Token is UAC-filtered, switched to linked token");
            CloseHandle(token);
            token = linked;
            DescribeToken("linked token", token);
        }

        // Primary token, bound to the target session.
        if (!DuplicateTokenEx(token, MAXIMUM_ALLOWED, IntPtr.Zero, 2 /*Impersonation*/, 1 /*Primary*/, out var primary))
            throw Win32("DuplicateTokenEx");
        CloseHandle(token);
        var sid = session;
        if (!SetTokenInformation(primary, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sid, sizeof(uint)))
            throw Win32("SetTokenInformation(TokenSessionId)");
        DescribeToken("primary token", primary);

        // --- 1.2: desktop access in the user's session
        var userSid = GetTokenUserSid(primary);
        if (!o.Has("no-desktop-acl"))
        {
            var rc = RunAsSystemInSession(session, $"grant-desktop --sid {userSid.Value}");
            Log($"grant-desktop helper exit code {rc}");
            if (rc != 0) Log("RESULT 1.2 desktop ACL: FAILED (see grant-desktop log)");
        }

        IntPtr profile = IntPtr.Zero;
        if (!o.Has("no-profile"))
        {
            var pi = new PROFILEINFO { dwSize = Marshal.SizeOf<PROFILEINFO>(), dwFlags = 1 /*PI_NOUI*/, lpUserName = user };
            if (LoadUserProfileW(primary, ref pi)) { profile = pi.hProfile; Log("LoadUserProfile: OK"); }
            else Log($"LoadUserProfile failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        if (!CreateEnvironmentBlock(out var env, primary, false)) throw Win32("CreateEnvironmentBlock");

        IntPtr job = IntPtr.Zero;
        if (o.Has("job"))
        {
            job = CreateJobObjectW(IntPtr.Zero, null);
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            limits.BasicLimitInformation.ActiveProcessLimit = 1;
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf(limits)))
                throw Win32("SetInformationJobObject");
            Log("Job object with ActiveProcessLimit=1 prepared");
        }

        var cmd = new StringBuilder($"\"{exe}\" {o.Get("args", "")}");
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
        var flags = CREATE_UNICODE_ENVIRONMENT | CREATE_SUSPENDED | CREATE_BREAKAWAY_FROM_JOB;
        if (!CreateProcessAsUserW(primary, null, cmd, IntPtr.Zero, IntPtr.Zero, false, flags, env,
                Path.GetDirectoryName(exe), ref si, out var proc))
            throw Win32("CreateProcessAsUserW");
        Log($"Process {proc.dwProcessId} created in session {session}");

        if (job != IntPtr.Zero && !AssignProcessToJobObject(job, proc.hProcess)) throw Win32("AssignProcessToJobObject");
        ResumeThread(proc.hThread);
        Log("RESULT 1.2 CreateProcessAsUser in user session: OK (check on screen whether the window is visible and usable)");

        if (o.Has("wait") || job != IntPtr.Zero)
        {
            // The job must outlive the process, otherwise the limit no longer applies.
            WaitForSingleObject(proc.hProcess, INFINITE);
            GetExitCodeProcess(proc.hProcess, out var code);
            Log($"Process exited with 0x{code:X8}");
            if (code == 0xC0000142) Log("RESULT 1.2: STATUS_DLL_INIT_FAILED -> no desktop access");
        }

        DestroyEnvironmentBlock(env);
        if (profile != IntPtr.Zero) UnloadUserProfile(primary, profile);
        CloseHandle(proc.hThread);
        CloseHandle(proc.hProcess);
        CloseHandle(primary);
        if (job != IntPtr.Zero) CloseHandle(job);
        return 0;
    }

    private static IntPtr S4ULogon(string user, string domain, SECURITY_LOGON_TYPE logonType)
    {
        var origin = Lsa("AppStartSpike");
        var status = LsaRegisterLogonProcess(ref origin, out var lsa, out _);
        Log($"LsaRegisterLogonProcess: 0x{status:X8}");
        if (status != STATUS_SUCCESS) throw new Win32Exception((int)LsaNtStatusToWinError(status), "LsaRegisterLogonProcess");

        var pkgName = Lsa("MICROSOFT_AUTHENTICATION_PACKAGE_V1_0"); // MSV1_0 handles S4U for local accounts
        status = LsaLookupAuthenticationPackage(lsa, ref pkgName, out var pkg);
        if (status != STATUS_SUCCESS) throw new Win32Exception((int)LsaNtStatusToWinError(status), "LsaLookupAuthenticationPackage");

        // MSV1_0_S4U_LOGON with both strings in the same buffer, right after the header.
        var userBytes = Encoding.Unicode.GetBytes(user);
        var domainBytes = Encoding.Unicode.GetBytes(domain);
        var headerSize = Marshal.SizeOf<MSV1_0_S4U_LOGON_HEADER>();
        var total = headerSize + userBytes.Length + domainBytes.Length;
        var buf = Marshal.AllocHGlobal(total);
        try
        {
            var header = new MSV1_0_S4U_LOGON_HEADER
            {
                MessageType = MSV1_0_S4U_LOGON,
                UserPrincipalName = new UNICODE_STRING { Length = (ushort)userBytes.Length, MaximumLength = (ushort)userBytes.Length, Buffer = buf + headerSize },
                DomainName = new UNICODE_STRING { Length = (ushort)domainBytes.Length, MaximumLength = (ushort)domainBytes.Length, Buffer = buf + headerSize + userBytes.Length },
            };
            Marshal.StructureToPtr(header, buf, false);
            Marshal.Copy(userBytes, 0, buf + headerSize, userBytes.Length);
            Marshal.Copy(domainBytes, 0, buf + headerSize + userBytes.Length, domainBytes.Length);

            var source = new TOKEN_SOURCE();
            var name = "AppStart"u8;
            for (var i = 0; i < 8 && i < name.Length; i++) source.SourceName[i] = name[i];

            status = LsaLogonUser(lsa, ref origin, logonType, pkg, buf, (uint)total, IntPtr.Zero, ref source,
                out var profileBuf, out _, out _, out var token, out _, out var sub);
            Log($"LsaLogonUser(S4U, {logonType}): status 0x{status:X8}, substatus 0x{sub:X8}");
            if (profileBuf != IntPtr.Zero) LsaFreeReturnBuffer(profileBuf);
            if (status != STATUS_SUCCESS)
            {
                Log("RESULT 1.1 S4U logon: FAILED -> fallback DPAPI password needed (try --password-env)");
                throw new Win32Exception((int)LsaNtStatusToWinError(status), "LsaLogonUser(S4U)");
            }
            return token;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
            LsaDeregisterLogonProcess(lsa);
        }
    }

    // Starts this EXE as SYSTEM in the given session (on its WinSta0\Default) and waits for it.
    private static int RunAsSystemInSession(uint session, string arguments)
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ALL_ACCESS, out var self)) throw Win32("OpenProcessToken");
        if (!DuplicateTokenEx(self, MAXIMUM_ALLOWED, IntPtr.Zero, 2, 1, out var dup)) throw Win32("DuplicateTokenEx(self)");
        CloseHandle(self);
        var s = session;
        if (!SetTokenInformation(dup, TOKEN_INFORMATION_CLASS.TokenSessionId, ref s, sizeof(uint))) throw Win32("SetTokenInformation(self)");
        var exe = Environment.ProcessPath!;
        var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
        var cmd = new StringBuilder($"\"{exe}\" {arguments}");
        if (!CreateProcessAsUserW(dup, null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0x08000000 /*CREATE_NO_WINDOW*/,
                IntPtr.Zero, null, ref si, out var pi))
            throw Win32("CreateProcessAsUserW(helper)");
        WaitForSingleObject(pi.hProcess, 30_000);
        GetExitCodeProcess(pi.hProcess, out var code);
        CloseHandle(pi.hThread); CloseHandle(pi.hProcess); CloseHandle(dup);
        return (int)code;
    }

    // ------------------------------------------------------------------ grant-desktop (SYSTEM in user session)
    private static int GrantDesktop(Options o)
    {
        var sid = new SecurityIdentifier(o.Require("sid"));
        Log($"grant-desktop in session {Process.GetCurrentProcess().SessionId} for {sid}");
        var winsta = OpenWindowStationW("WinSta0", false, READ_CONTROL | WRITE_DAC | WINSTA_ALL_ACCESS);
        if (winsta == IntPtr.Zero) throw Win32("OpenWindowStation");
        if (!SetProcessWindowStation(winsta)) throw Win32("SetProcessWindowStation");
        var desktop = OpenDesktopW("Default", 0, false, READ_CONTROL | WRITE_DAC | DESKTOP_ALL_ACCESS);
        if (desktop == IntPtr.Zero) throw Win32("OpenDesktop");

        AddAce(winsta, sid, (int)WINSTA_ALL_ACCESS);
        AddAce(desktop, sid, (int)DESKTOP_ALL_ACCESS);
        Log("RESULT 1.2 desktop ACL: granted on WinSta0 and Default");
        CloseDesktop(desktop);
        CloseWindowStation(winsta);
        return 0;
    }

    private static void AddAce(IntPtr handle, SecurityIdentifier sid, int mask)
    {
        var sec = new UserObjectSecurity(handle);
        sec.AddAccessRule(new UserObjectAccessRule(sid, mask, AccessControlType.Allow));
        sec.Persist(handle);
    }

    private sealed class UserObjectSecurity(IntPtr handle)
        : NativeObjectSecurity(false, ResourceType.WindowObject, new Microsoft.Win32.SafeHandles.SafeWaitHandle(handle, false), AccessControlSections.Access)
    {
        public override Type AccessRightType => typeof(int);
        public override Type AccessRuleType => typeof(UserObjectAccessRule);
        public override Type AuditRuleType => typeof(AuditRule);
        public new void AddAccessRule(AccessRule rule) => base.AddAccessRule(rule);
        public void Persist(IntPtr h) => base.Persist(new Microsoft.Win32.SafeHandles.SafeWaitHandle(h, false), AccessControlSections.Access);
        public override AccessRule AccessRuleFactory(IdentityReference id, int mask, bool inh, InheritanceFlags i, PropagationFlags p, AccessControlType t)
            => new UserObjectAccessRule(id, mask, t);
        public override AuditRule AuditRuleFactory(IdentityReference id, int mask, bool inh, InheritanceFlags i, PropagationFlags p, AuditFlags f)
            => throw new NotSupportedException();
    }

    private sealed class UserObjectAccessRule(IdentityReference id, int mask, AccessControlType type)
        : AccessRule(id, mask, false, InheritanceFlags.None, PropagationFlags.None, type);

    // ------------------------------------------------------------------ probe (as app account)
    private static int Probe()
    {
        var id = WindowsIdentity.GetCurrent();
        var admin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        var lines = new List<string>
        {
            $"PROBE account      : {id.Name}",
            $"PROBE session      : {Process.GetCurrentProcess().SessionId}",
            $"PROBE admin (full) : {admin}",
            $"PROBE profile      : {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}",
        };
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\AppStartSpike");
            k.SetValue("probe", DateTime.Now.ToString("O"));
            lines.Add($"PROBE HKCU write   : OK ({k.Name})");
        }
        catch (Exception ex) { lines.Add($"PROBE HKCU write   : FAILED {ex.Message}"); }

        try
        {
            using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true });
            p!.WaitForExit();
            lines.Add("PROBE child cmd.exe: STARTED");
        }
        catch (Exception ex) { lines.Add($"PROBE child cmd.exe: BLOCKED ({ex.Message})"); }

        foreach (var line in lines) Log(line);

        // Visible proof for 1.2: a message box on the operator's desktop, closes itself after 15 s.
        var rc = MessageBoxTimeoutW(IntPtr.Zero, string.Join("\n", lines), "AppStart Spike – probe", 0x40 /*MB_ICONINFORMATION*/, 0, 15_000);
        Log(rc == 0 ? $"PROBE message box  : FAILED (error {Marshal.GetLastWin32Error()})" : $"PROBE message box  : shown (rc {rc})");
        return 0;
    }

    // ------------------------------------------------------------------ helpers
    private static void DescribeToken(string label, IntPtr token)
    {
        using var wi = new WindowsIdentity(token);
        var elevType = GetTokenUInt(token, TOKEN_INFORMATION_CLASS.TokenElevationType);
        var elevated = GetTokenUInt(token, TOKEN_INFORMATION_CLASS.TokenElevation);
        Log($"{label}: user {wi.Name}, elevationType {elevType} (1=Default,2=Full,3=Limited), elevated {elevated}, " +
            $"session {GetTokenUInt(token, TOKEN_INFORMATION_CLASS.TokenSessionId)}, " +
            $"admin group {wi.Groups?.Any(g => g.Value == "S-1-5-32-544")}");
    }

    private static SecurityIdentifier GetTokenUserSid(IntPtr token)
    {
        using var wi = new WindowsIdentity(token);
        return wi.User!;
    }

    private static uint GetTokenUInt(IntPtr token, TOKEN_INFORMATION_CLASS cls)
    {
        var buf = Marshal.AllocHGlobal(4);
        try
        {
            if (!GetTokenInformation(token, cls, buf, 4, out _)) throw Win32($"GetTokenInformation({cls})");
            return (uint)Marshal.ReadInt32(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IntPtr GetTokenHandle(IntPtr token, TOKEN_INFORMATION_CLASS cls)
    {
        var buf = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (!GetTokenInformation(token, cls, buf, IntPtr.Size, out _)) throw Win32($"GetTokenInformation({cls})");
            return Marshal.ReadIntPtr(buf);
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static void EnablePrivilege(string name)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0020 | 0x0008 /*ADJUST|QUERY*/, out var tok)) throw Win32("OpenProcessToken");
        if (!LookupPrivilegeValueW(null, name, out var luid)) throw Win32("LookupPrivilegeValue");
        // TOKEN_PRIVILEGES with one LUID_AND_ATTRIBUTES: count(4) + luid(8) + attributes(4)
        var tp = Marshal.AllocHGlobal(16);
        Marshal.WriteInt32(tp, 0, 1);
        Marshal.WriteInt32(tp, 4, (int)luid.LowPart);
        Marshal.WriteInt32(tp, 8, luid.HighPart);
        Marshal.WriteInt32(tp, 12, 2 /*SE_PRIVILEGE_ENABLED*/);
        AdjustTokenPrivileges(tok, false, tp, 16, IntPtr.Zero, IntPtr.Zero);
        var err = Marshal.GetLastWin32Error();
        Log($"Enable {name}: {(err == 0 ? "OK" : $"error {err}")}");
        Marshal.FreeHGlobal(tp);
        CloseHandle(tok);
    }

    private static LSA_STRING Lsa(string s)
    {
        var bytes = Encoding.ASCII.GetBytes(s);
        var p = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, p, bytes.Length);
        Marshal.WriteByte(p, bytes.Length, 0);
        return new LSA_STRING { Length = (ushort)bytes.Length, MaximumLength = (ushort)(bytes.Length + 1), Buffer = p };
    }

    private static Win32Exception Win32(string what)
    {
        var err = Marshal.GetLastWin32Error();
        var ex = new Win32Exception(err, $"{what} failed: {new Win32Exception(err).Message}");
        Log(ex.Message);
        return ex;
    }

    private static void Log(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        Console.WriteLine(line);
        _log?.WriteLine(line);
    }

    private sealed class Options
    {
        private readonly Dictionary<string, string> _v = new(StringComparer.OrdinalIgnoreCase);

        public static Options Parse(IEnumerable<string> args)
        {
            var o = new Options();
            var list = args.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                if (!list[i].StartsWith("--")) continue;
                var key = list[i][2..];
                var hasValue = i + 1 < list.Count && !list[i + 1].StartsWith("--");
                o._v[key] = hasValue ? list[++i] : "true";
            }
            return o;
        }

        public bool Has(string k) => _v.ContainsKey(k);
        public string Get(string k, string def = "") => _v.TryGetValue(k, out var v) ? v : def;
        public string Require(string k) => _v.TryGetValue(k, out var v) ? v : throw new ArgumentException($"--{k} missing");
    }
}
