using System.CommandLine;
using System.Security;
using AppStart.Core;
using AppStart.Setup.Engine;
using Windows.Win32;

namespace AppStart.Setup.Cli;

/// <summary>
/// Command line (task 6.6) for silent installation and the service technician:
/// add, add-user, remove, list, rehash, verify, uninstall. Exit code 0 = success, 1 = failure or
/// deviation, 2 = wrong usage. Runs elevated (manifest); call it from an elevated shell so that the
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
        var add = new Command("add", CliTexts.AddDescription) { exe, dataDir, regKey, name };
        add.SetAction(r => Print(engineFactory().Add(r.GetValue(exe)!.FullName, r.GetValue(dataDir), r.GetValue(regKey), r.GetValue(name))));
        root.Subcommands.Add(add);

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
        remove.SetAction(r => Print(engineFactory().Remove(r.GetValue(removeApp)!)));
        root.Subcommands.Add(remove);

        var rehashApp = new Option<string>("--app") { Description = CliTexts.AppOption, Required = true };
        var rehash = new Command("rehash", CliTexts.RehashDescription) { rehashApp };
        rehash.SetAction(r => Print(engineFactory().Rehash(r.GetValue(rehashApp)!)));
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
            return Print(engine.Apply(new SetupRequest(apps, false, null, operators)));
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

        return Print(engine.Apply(new SetupRequest(apps, true, password, current.Operators)));
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
            Console.WriteLine($"{string.Empty,-20} {app.Exe} ({(app.Check == Core.Configuration.IntegrityCheck.Signature ? "Signatur" : "SHA-256")})");
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
