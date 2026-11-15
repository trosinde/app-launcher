using System.CommandLine;
using System.Security;
using AppStart.Core;
using AppStart.Core.Configuration;
using AppStart.Setup.Engine;
using Windows.Win32;

namespace AppStart.Setup.Cli;

/// <summary>
/// Command line (task 6.6) for silent installation and the service technician:
/// add, add-user, remove, list, rehash, verify, uninstall. Exit code 0 = success, 1 = failure, deviation
/// or invalid arguments (System.CommandLine), 2 = inconsistent options of add-user/startmenu. Runs elevated (manifest); call it from an elevated shell so that the
/// output appears in that console.
/// </summary>
internal static class CommandLine
{
    public static int Run(string[] args)
    {
        AttachToParentConsole();
        return Build(() => Integration.SetupComposition.CreateEngine()).Parse(args).Invoke();
    }

    /// <summary>Command tree; the engine factory is injectable for tests.</summary>
    internal static RootCommand Build(Func<SetupEngine> engineFactory)
    {
        var root = new RootCommand(CliTexts.RootDescription);

        // add --exe <path> [--data-dir <folder>] [--reg-key <key>] [--name <display name>]
        var exe = new Option<FileInfo>("--exe") { Description = CliTexts.ExeOption, Required = true };
        var dataDir = new Option<string?>("--data-dir") { Description = CliTexts.DataDirOption };
        var regKey = new Option<string?>("--reg-key") { Description = CliTexts.RegKeyOption };
        var name = new Option<string?>("--name") { Description = CliTexts.NameOption };
        var id = new Option<string?>("--id") { Description = CliTexts.IdOption };
        var addMode = new Option<ChildProcessMode?>("--child-processes") { Description = CliTexts.ChildProcessesOption };
        var add = new Command("add", CliTexts.AddDescription) { exe, dataDir, regKey, name, id, addMode };
        add.SetAction(r => Changed(engineFactory, e => e.Add(r.GetValue(exe)!.FullName, r.GetValue(dataDir), r.GetValue(regKey), r.GetValue(name),
            r.GetValue(id), r.GetValue(addMode))));
        root.Subcommands.Add(add);

        // add-allowed-child-processes, design D4: helper programs and learn mode
        var modeApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var mode = new Option<ChildProcessMode>("--mode") { Description = CliTexts.ModeOption, Required = true };
        var setChildren = new Command("set-children", CliTexts.SetChildrenDescription) { modeApp, mode };
        setChildren.SetAction(r => Changed(engineFactory, e => e.SetChildMode(r.GetValue(modeApp)!, r.GetValue(mode))));
        root.Subcommands.Add(setChildren);

        var allowApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var allowExe = new Option<FileInfo>("--exe") { Description = CliTexts.ChildExeOption, Required = true };
        var check = new Option<IntegrityCheck?>("--check") { Description = CliTexts.CheckOption };
        var allowShell = new Option<bool>("--allow-shell") { Description = CliTexts.AllowShellOption };
        var allowChild = new Command("allow-child", CliTexts.AllowChildDescription) { allowApp, allowExe, check, allowShell };
        allowChild.SetAction(r => Changed(engineFactory, e => e.AllowChild(r.GetValue(allowApp)!, r.GetValue(allowExe)!.FullName, r.GetValue(check), r.GetValue(allowShell))));
        root.Subcommands.Add(allowChild);

        var denyApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var denyExe = new Option<FileInfo>("--exe") { Description = CliTexts.ChildExeOption, Required = true };
        var denyChild = new Command("deny-child", CliTexts.DenyChildDescription) { denyApp, denyExe };
        denyChild.SetAction(r => Changed(engineFactory, e => e.DenyChild(r.GetValue(denyApp)!, r.GetValue(denyExe)!.FullName)));
        root.Subcommands.Add(denyChild);

        root.Subcommands.Add(BuildLearn(engineFactory));

        // add-user --name appstart-user [--password-stdin] | --member "<DOMAIN\User>"
        var userName = new Option<string?>("--name") { Description = CliTexts.UserNameOption };
        var member = new Option<string?>("--member") { Description = CliTexts.MemberOption };
        var passwordStdin = new Option<bool>("--password-stdin") { Description = CliTexts.PasswordStdinOption };
        var addUser = new Command("add-user", CliTexts.AddUserDescription) { userName, member, passwordStdin };
        addUser.SetAction(r => AddUser(engineFactory, r.GetValue(userName), r.GetValue(member), r.GetValue(passwordStdin)));
        root.Subcommands.Add(addUser);

        // remove --app <AppId> / rehash --app <AppId>
        var removeApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var remove = new Command("remove", CliTexts.RemoveDescription) { removeApp };
        remove.SetAction(r => Changed(engineFactory, e => e.Remove(r.GetValue(removeApp)!)));
        root.Subcommands.Add(remove);

        var rehashApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var rehashChild = new Option<FileInfo?>("--child") { Description = CliTexts.RehashChildOption };
        var rehash = new Command("rehash", CliTexts.RehashDescription) { rehashApp, rehashChild };
        rehash.SetAction(r => Changed(engineFactory, e => r.GetValue(rehashChild) is { } child
            ? e.RehashChild(r.GetValue(rehashApp)!, child.FullName)
            : e.Rehash(r.GetValue(rehashApp)!)));
        root.Subcommands.Add(rehash);

        var list = new Command("list", CliTexts.ListDescription);
        list.SetAction(_ => List(engineFactory()));
        root.Subcommands.Add(list);

        // verify [--report <path>] [--ui]
        var report = new Option<FileInfo?>("--report") { Description = CliTexts.ReportOption };
        var ui = new Option<bool>("--ui") { Description = CliTexts.UiOption };
        var verify = new Command("verify", CliTexts.VerifyDescription) { report, ui };
        verify.SetAction(r => r.GetValue(ui) ? Views.WizardLauncher.ShowVerify(engineFactory()) : Verify(engineFactory(), r.GetValue(report)));
        root.Subcommands.Add(verify);

        var uninstall = new Command("uninstall", CliTexts.UninstallDescription);
        uninstall.SetAction(_ => Print(engineFactory().Uninstall()));
        root.Subcommands.Add(uninstall);

        // Hidden: called by the Velopack hooks of the service (design D5) for AppStart's own Start menu entries.
        var create = new Option<bool>("--create");
        var removeEntries = new Option<bool>("--remove");
        var startMenu = new Command("startmenu") { Hidden = true };
        startMenu.Options.Add(create);
        startMenu.Options.Add(removeEntries);
        startMenu.SetAction(r => StartMenu.Apply(r.GetValue(create), r.GetValue(removeEntries)));
        root.Subcommands.Add(startMenu);

        return root;
    }

    // learn start | stop | show | apply (design D3/D4)
    private static Command BuildLearn(Func<SetupEngine> engineFactory)
    {
        var learn = new Command("learn", CliTexts.LearnDescription);

        var startApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var minutes = new Option<int>("--minutes") { Description = CliTexts.MinutesOption, DefaultValueFactory = _ => Core.Learning.LearnState.DefaultMinutes };
        var start = new Command("start", CliTexts.LearnStartDescription) { startApp, minutes };
        start.SetAction(r => Unchecked(() => Print(engineFactory().StartLearning(r.GetValue(startApp)!, r.GetValue(minutes)))));
        learn.Subcommands.Add(start);

        var stopApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var stop = new Command("stop", CliTexts.LearnStopDescription) { stopApp };
        stop.SetAction(r => Unchecked(() => Print(engineFactory().StopLearning(r.GetValue(stopApp)!))));
        learn.Subcommands.Add(stop);

        var showApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var show = new Command("show", CliTexts.LearnShowDescription) { showApp };
        show.SetAction(r => Unchecked(() => LearnShow(engineFactory(), r.GetValue(showApp)!)));
        learn.Subcommands.Add(show);

        var applyApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var all = new Option<bool>("--all") { Description = CliTexts.LearnAllOption };
        var exes = new Option<FileInfo[]>("--exe") { Description = CliTexts.LearnExeOption, AllowMultipleArgumentsPerToken = true };
        var apply = new Command("apply", CliTexts.LearnApplyDescription) { applyApp, all, exes };
        apply.SetAction(r =>
        {
            var chosen = r.GetValue(exes) ?? [];
            if (r.GetValue(all) == (chosen.Length > 0))
            {
                Console.Error.WriteLine(CliTexts.LearnApplyUsage);
                return 2;
            }

            return Changed(engineFactory, e => e.ApplyLearned(r.GetValue(applyApp)!, r.GetValue(all) ? null : [.. chosen.Select(f => f.FullName)]));
        });
        learn.Subcommands.Add(apply);
        return learn;
    }

    private static int LearnShow(SetupEngine engine, string appId)
    {
        var session = engine.GetLearnSession(appId);
        Console.WriteLine(session is null
            ? string.Format(CliTexts.LearnInactive, appId)
            : string.Format(CliTexts.LearnActiveUntil, appId, session.ExpiresUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture)));
        var recording = engine.GetRecording(appId);
        if (recording is not { Programs.Count: > 0 })
        {
            Console.WriteLine(CliTexts.LearnEmpty);
            return 0;
        }

        foreach (var p in recording.Programs.OrderBy(p => p.FirstStartUtc))
        {
            var mark = p.IsShellOrScriptHost ? CliTexts.ShellMark : string.Empty;
            Console.WriteLine($"{p.Exe}{mark}");
            Console.WriteLine(string.Format(CliTexts.LearnProgramLine, p.Starts,
                p.SignatureValid ? CliTexts.CheckSignature : CliTexts.CheckSha256,
                p.LastStartUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return 0;
    }

    // Invalid input found by AppStart itself (not by the parser): message on stderr, exit code 2, nothing changed.
    private static int Unchecked(Func<int> action)
    {
        try
        {
            return action();
        }
        catch (SetupInputException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }

    private static int AddUser(Func<SetupEngine> engineFactory, string? name, string? member, bool passwordFromStdin)
    {
        if ((name is null) == (member is null))
        {
            Console.Error.WriteLine(CliTexts.AddUserUsage);
            return 2;
        }

        var engine = engineFactory();
        var current = engine.LoadCurrent();
        var apps = current.Apps.Select(a => engine.Inspect(a.Exe)).ToList();
        if (member is not null)
        {
            var operators = current.Operators.Append(member).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return Changed(engine, engine.Apply(new SetupRequest(apps, false, null, operators)));
        }

        if (!string.Equals(name, AppStartConstants.OperatorAccountName, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(CliTexts.AddUserNameOnly);
            return 2;
        }

        using var password = passwordFromStdin ? ReadPasswordLine() : PromptPassword();
        if (password is null)
        {
            Console.Error.WriteLine(CliTexts.PasswordMismatch);
            return 2;
        }

        return Changed(engine, engine.Apply(new SetupRequest(apps, true, password, current.Operators)));
    }

    private static int List(SetupEngine engine)
    {
        var current = engine.LoadCurrent();
        if (current.Apps.Count == 0)
        {
            Console.WriteLine(CliTexts.NoApps);
        }

        foreach (var app in current.Apps)
        {
            Console.WriteLine($"{app.Id,-20} {app.Name}");
            Console.WriteLine($"{string.Empty,-20} {app.Exe} ({CheckText(app.Check)})");
            Console.WriteLine($"{string.Empty,-20} {string.Format(CliTexts.ChildModeLine, ModeText(app.ChildProcessMode))}");
            foreach (var child in app.AllowedChildren ?? [])
            {
                Console.WriteLine($"{string.Empty,-20}   {child.Exe} ({CheckText(child.Check)}{(child.AllowShell ? CliTexts.ShellMark : string.Empty)})");
            }
        }

        Console.WriteLine();
        Console.WriteLine(string.Format(CliTexts.OperatorsLine, current.Operators.Count == 0 ? "–" : string.Join(", ", current.Operators)));
        Console.WriteLine(string.Format(CliTexts.ServiceLine, current.ServiceRunning ? CliTexts.Running : CliTexts.NotRunning));
        return 0;
    }

    private static int Verify(SetupEngine engine, FileInfo? reportCopy)
    {
        var report = engine.Verify();
        var html = engine.WriteReport(report);
        if (reportCopy is not null)
        {
            File.Copy(html, reportCopy.FullName, overwrite: true);
        }

        foreach (var check in report.Checks)
        {
            Console.WriteLine($"{(check.Outcome == CheckOutcome.Passed ? "OK        " : "ABWEICHUNG")}  {check.Title}");
            foreach (var finding in check.Findings)
            {
                Console.WriteLine($"            {finding}");
            }
        }

        Console.WriteLine();
        Console.WriteLine(report.Deviations == 0
            ? string.Format(CliTexts.VerifyPassed, report.Checks.Count)
            : string.Format(CliTexts.VerifyFailed, report.Deviations));
        Console.WriteLine(string.Format(CliTexts.ReportSaved, reportCopy?.FullName ?? html));
        return report.ExitCode;
    }

    private static int Changed(Func<SetupEngine> engineFactory, Func<SetupEngine, IReadOnlyList<SetupStepResult>> change)
    {
        var engine = engineFactory();
        IReadOnlyList<SetupStepResult> results;
        try
        {
            results = change(engine);
        }
        catch (SetupInputException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        return Changed(engine, results);
    }

    private static string CheckText(IntegrityCheck check) => check == IntegrityCheck.Signature ? CliTexts.CheckSignature : CliTexts.CheckSha256;

    private static string ModeText(ChildProcessMode mode) => mode switch
    {
        ChildProcessMode.Allowlist => SetupEngineTexts.ModeAllowlist,
        ChildProcessMode.All => SetupEngineTexts.ModeAll,
        _ => SetupEngineTexts.ModeBlocked,
    };

    // Task 9.3: every change is followed by an automatic check with a stored report.
    private static int Changed(SetupEngine engine, IReadOnlyList<SetupStepResult> results)
    {
        var changeCode = Print(results);
        Console.WriteLine();
        Console.WriteLine(CliTexts.AutoVerify);
        var verifyCode = Verify(engine, null);
        return changeCode != 0 ? changeCode : verifyCode;
    }

    private static int Print(IReadOnlyList<SetupStepResult> results)
    {
        foreach (var r in results)
        {
            Console.WriteLine(r.Success ? $"✓ {r.Text}" : $"✗ {r.Text}{(r.Detail is null ? string.Empty : ": " + r.Detail)}");
        }

        return results.All(r => r.Success) ? 0 : 1;
    }

    private static SecureString? ReadPasswordLine()
    {
        var line = Console.In.ReadLine();
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var secure = new SecureString();
        foreach (var c in line)
        {
            secure.AppendChar(c);
        }

        secure.MakeReadOnly();
        return secure;
    }

    private static SecureString? PromptPassword()
    {
        Console.Write(CliTexts.PasswordPrompt);
        using var first = ReadMasked();
        Console.Write(CliTexts.PasswordRepeat);
        using var second = ReadMasked();
        return first.Length > 0 && SecureEquals(first, second) ? first.Copy() : null;
    }

    private static SecureString ReadMasked()
    {
        var secure = new SecureString();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return secure;
            }

            if (key.Key == ConsoleKey.Backspace && secure.Length > 0)
            {
                secure.RemoveAt(secure.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secure.AppendChar(key.KeyChar);
            }
        }
    }

    private static bool SecureEquals(SecureString a, SecureString b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }

        var pa = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(a);
        var pb = System.Runtime.InteropServices.Marshal.SecureStringToGlobalAllocUnicode(b);
        try
        {
            for (var i = 0; i < a.Length; i++)
            {
                if (System.Runtime.InteropServices.Marshal.ReadInt16(pa, i * 2) != System.Runtime.InteropServices.Marshal.ReadInt16(pb, i * 2))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(pa);
            System.Runtime.InteropServices.Marshal.ZeroFreeGlobalAllocUnicode(pb);
        }
    }

    // WinExe has no console of its own; attach to the calling one so that output and prompts work.
    private static void AttachToParentConsole()
    {
        if (PInvoke.AttachConsole(PInvoke.ATTACH_PARENT_PROCESS))
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (IOException)
            {
            }
        }
    }
}
