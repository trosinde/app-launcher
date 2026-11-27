using AppStart.Core.Configuration;

namespace AppStart.Core.Tests;

public class AppsConfigSerializerTests
{
    // Example from the concept (Konfiguration), with a real-looking thumbprint.
    private const string ConceptExample = """
        {
          "version": 1,
          "apps": [
            {
              "id": "app-a",
              "name": "Application A",
              "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
              "workingDir": "C:\\Program Files\\Hersteller\\AppA",
              "check": "signature",
              "signerThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567",
              "sha256": null,
              "allowChildProcesses": false,
              "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
              "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
            }
          ]
        }
        """;

    [Fact]
    public void Concept_example_is_valid()
    {
        var result = AppsConfigSerializer.Parse(ConceptExample);

        Assert.True(result.IsValid, result.Error);
        var app = Assert.Single(result.Config!.Apps);
        Assert.Equal("app-a", app.Id);
        Assert.Equal(IntegrityCheck.Signature, app.Check);
        Assert.False(app.AllowChildProcesses);
        Assert.Equal(ChildProcessMode.Blocked, app.ChildProcessMode);
        Assert.Same(app, result.Config.Find("app-a"));
        Assert.Null(result.Config.Find("APP-A"));
    }

    [Fact]
    public void Roundtrip_keeps_content()
    {
        var config = AppsConfigSerializer.Parse(ConceptExample).Config!;
        var again = AppsConfigSerializer.Parse(AppsConfigSerializer.Serialize(config));
        Assert.True(again.IsValid, again.Error);
        Assert.Equal(config.Apps[0], again.Config!.Apps[0]);
    }

    [Theory]
    [InlineData("\"id\": \"app-a\"", "\"id\": \"App-A\"", "apps[0].id")]
    [InlineData("\"check\": \"signature\"", "\"check\": \"sha256\"", "apps[0].sha256")]
    [InlineData("\"signerThumbprint\": \"0123456789ABCDEF0123456789ABCDEF01234567\"", "\"signerThumbprint\": \"0123\"", "apps[0].signerThumbprint")]
    [InlineData("\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.exe\"", "\"exe\": \"AppA.exe\"", "apps[0].exe")]
    [InlineData("\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.exe\"", "\"exe\": \"\\\\\\\\server\\\\share\\\\AppA.exe\"", "apps[0].exe")]
    [InlineData("\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.exe\"", "\"exe\": \"C:\\\\Program Files\\\\..\\\\Windows\\\\System32\\\\cmd.exe\"", "apps[0].exe")]
    [InlineData("\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.exe\"", "\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.bat\"", "apps[0].exe")]
    [InlineData("\"registryKey\": \"HKLM\\\\SOFTWARE\\\\Hersteller\\\\AppA\"", "\"registryKey\": \"HKCU\\\\Software\\\\AppA\"", "apps[0].registryKey")]
    [InlineData("\"version\": 1", "\"version\": 3", "version")]
    [InlineData("\"version\": 1", "\"version\": 0", "version")]
    [InlineData("\"version\": 1", "\"version\": 2", "apps[0].allowChildProcesses")]
    [InlineData("\"allowChildProcesses\": false", "\"childProcesses\": \"blocked\"", "apps[0].childProcesses")]
    [InlineData("\"name\": \"Applikation A\"", "\"name\": \" \"", "apps[0].name")]
    public void Invalid_field_is_reported(string from, string to, string expectedField)
    {
        var json = ConceptExample.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(ConceptExample, json);

        var result = AppsConfigSerializer.Parse(json);

        Assert.False(result.IsValid);
        Assert.StartsWith(expectedField, result.Error);
    }

    [Fact]
    public void Duplicate_app_id_is_rejected()
    {
        var config = AppsConfigSerializer.Parse(ConceptExample).Config!;
        var doubled = config with { Apps = [config.Apps[0], config.Apps[0] with { Name = "B" }] };

        var error = AppsConfigSerializer.Validate(doubled);

        Assert.Equal("apps[1].id: \"app-a\" occurs more than once.", error);
        Assert.Throws<ArgumentException>(() => AppsConfigSerializer.Serialize(doubled));
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"apps\": [], \"extra\": true }")]          // unknown property
    [InlineData("{ \"apps\": [] }")]                                          // version missing
    [InlineData("{ \"version\": 1, \"apps\": [], }")]                         // trailing comma
    [InlineData("{ // comment\n \"version\": 1, \"apps\": [] }")]             // comment
    [InlineData("{ \"version\": 1, \"apps\": [ { \"id\": \"a\" } ] }")]        // required fields missing
    [InlineData("{ \"version\": 1, \"apps\": [ { \"id\": \"a\", \"name\": \"A\", \"exe\": \"C:\\\\a.exe\", \"workingDir\": \"C:\\\\\", \"check\": \"none\" } ] }")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Malformed_json_is_rejected(string json)
    {
        var result = AppsConfigSerializer.Parse(json);
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Fact]
    public void Empty_app_list_is_valid() =>
        Assert.True(AppsConfigSerializer.Parse("{ \"version\": 1, \"apps\": [] }").IsValid);

    // ------------------------------------------------ add-allowed-child-processes, design D1

    private const string Thumb = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string Sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    // Example from design.md D1.
    private const string Version2Example = """
        {
          "version": 2,
          "apps": [{
            "id": "appa", "name": "Application A",
            "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
            "workingDir": "C:\\Program Files\\Hersteller\\AppA",
            "check": "signature", "signerThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567",
            "childProcesses": "allowlist",
            "allowedChildren": [
              { "exe": "C:\\Program Files\\Hersteller\\AppA\\Export.exe", "check": "signature", "signerThumbprint": "0123456789ABCDEF0123456789ABCDEF01234567" },
              { "exe": "C:\\Program Files\\Hersteller\\AppA\\Updater.exe", "check": "sha256", "sha256": "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF" }
            ],
            "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
            "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA"
          }]
        }
        """;

    private static AppEntry V2App(ChildProcessMode? mode, params AllowedChild[]? children) => new()
    {
        Id = "appa",
        Name = "Application A",
        Exe = @"C:\Program Files\Hersteller\AppA\AppA.exe",
        WorkingDir = @"C:\Program Files\Hersteller\AppA",
        Check = IntegrityCheck.Signature,
        SignerThumbprint = Thumb,
        ChildProcesses = mode,
        AllowedChildren = children is { Length: 0 } ? null : children, // params gives [] for "no children"
    };

    private static AllowedChild Child(string exe, bool allowShell = false) =>
        new() { Exe = exe, Check = IntegrityCheck.Sha256, Sha256 = Sha, AllowShell = allowShell };

    private static string? Validate(AppEntry app) =>
        AppsConfigSerializer.Validate(new AppsConfig { Version = 2, Apps = [app] });

    [Fact]
    public void Version_2_example_from_the_design_is_valid()
    {
        var result = AppsConfigSerializer.Parse(Version2Example);

        Assert.True(result.IsValid, result.Error);
        var app = Assert.Single(result.Config!.Apps);
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcessMode);
        Assert.Equal(2, app.AllowedChildren!.Count);
        Assert.Equal(IntegrityCheck.Sha256, app.AllowedChildren[1].Check);
        Assert.False(app.AllowedChildren[0].AllowShell);
    }

    [Theory]
    [InlineData(false, ChildProcessMode.Blocked)]
    [InlineData(true, ChildProcessMode.All)]
    public void Version_1_flag_maps_to_mode(bool allow, ChildProcessMode expected)
    {
        var json = ConceptExample.Replace("\"allowChildProcesses\": false", $"\"allowChildProcesses\": {(allow ? "true" : "false")}", StringComparison.Ordinal);
        var config = AppsConfigSerializer.Parse(json).Config!;

        Assert.Equal(expected, config.Apps[0].ChildProcessMode);

        var migrated = config.ToCurrentVersion();
        Assert.Equal(2, migrated.Version);
        Assert.Null(migrated.Apps[0].AllowChildProcesses);
        Assert.Equal(expected, migrated.Apps[0].ChildProcesses);
        Assert.Null(AppsConfigSerializer.Validate(migrated));
    }

    [Fact]
    public void Version_1_without_flag_is_blocked() =>
        Assert.Equal(ChildProcessMode.Blocked, AppsConfigSerializer.Parse(
            ConceptExample.Replace("\"allowChildProcesses\": false,", string.Empty, StringComparison.Ordinal)).Config!.Apps[0].ChildProcessMode);

    [Fact]
    public void Missing_mode_in_version_2_means_blocked() =>
        Assert.Null(Validate(V2App(null)));

    [Fact]
    public void Version_2_writes_no_version_1_field_and_roundtrips()
    {
        var config = AppsConfigSerializer.Parse(Version2Example).Config!;
        var json = AppsConfigSerializer.Serialize(config);

        Assert.DoesNotContain("allowChildProcesses", json, StringComparison.Ordinal);
        Assert.DoesNotContain("allowShell", json, StringComparison.Ordinal); // default false is not written
        var again = AppsConfigSerializer.Parse(json).Config!;
        Assert.Equal(config.Apps[0].AllowedChildren, again.Apps[0].AllowedChildren!);
    }

    [Fact]
    public void Migration_keeps_version_2_unchanged()
    {
        var config = AppsConfigSerializer.Parse(Version2Example).Config!;
        Assert.Same(config, config.ToCurrentVersion());
    }

    [Theory]
    [InlineData(ChildProcessMode.Blocked)]
    [InlineData(ChildProcessMode.All)]
    public void Children_only_with_allowlist(ChildProcessMode mode) =>
        Assert.StartsWith("apps[0].allowedChildren: only allowed with", Validate(V2App(mode, Child(@"C:\Program Files\A\Export.exe"))));

    [Fact]
    public void Allowlist_needs_at_least_one_entry()
    {
        Assert.StartsWith("apps[0].allowedChildren: childProcesses \"allowlist\" needs at least", Validate(V2App(ChildProcessMode.Allowlist)));
        Assert.StartsWith("apps[0].allowedChildren: childProcesses \"allowlist\" needs at least", Validate(V2App(ChildProcessMode.Allowlist, [])));
    }

    [Theory]
    [InlineData(@"Export.exe")]
    [InlineData(@"\\server\share\Export.exe")]
    [InlineData(@"C:\Program Files\A\..\..\Windows\System32\Export.exe")]
    [InlineData(@"%ProgramFiles%\A\Export.exe")]
    [InlineData(@"C:\Program Files\A\*.exe")]
    [InlineData(@"C:\Program Files\A\Export.bat")]
    public void Child_path_must_be_absolute_exe_without_placeholders(string exe) =>
        Assert.StartsWith("apps[0].allowedChildren[0].exe:", Validate(V2App(ChildProcessMode.Allowlist, Child(exe))));

    [Fact]
    public void Child_path_is_unique_per_app_ignoring_case() =>
        Assert.StartsWith("apps[0].allowedChildren[1].exe:", Validate(V2App(ChildProcessMode.Allowlist,
            Child(@"C:\Program Files\A\Export.exe"), Child(@"C:\PROGRAM FILES\A\export.exe"))));

    [Fact]
    public void Child_check_values_follow_the_main_exe_rules()
    {
        Assert.StartsWith("apps[0].allowedChildren[0].signerThumbprint:", Validate(V2App(ChildProcessMode.Allowlist,
            new AllowedChild { Exe = @"C:\Program Files\A\Export.exe", Check = IntegrityCheck.Signature, SignerThumbprint = "0123" })));
        Assert.StartsWith("apps[0].allowedChildren[0].sha256:", Validate(V2App(ChildProcessMode.Allowlist,
            new AllowedChild { Exe = @"C:\Program Files\A\Export.exe", Check = IntegrityCheck.Sha256 })));
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\Program Files\PowerShell\7\pwsh.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Program Files\A\RUNDLL32.EXE")] // renamed or copied host is caught by file name
    public void Shell_needs_allow_shell(string exe)
    {
        Assert.StartsWith("apps[0].allowedChildren[0].allowShell:", Validate(V2App(ChildProcessMode.Allowlist, Child(exe))));
        Assert.Null(Validate(V2App(ChildProcessMode.Allowlist, Child(exe, allowShell: true))));
    }

    [Fact]
    public void Shell_list_matches_design_d3()
    {
        string[] expected = ["cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "explorer.exe"];
        Assert.Equal(expected.Order(), ChildProcessRules.ShellsAndScriptHosts.Order(StringComparer.OrdinalIgnoreCase));
        Assert.False(ChildProcessRules.IsShellOrScriptHost(@"C:\Program Files\A\Export.exe"));
    }

    [Fact]
    public void Unknown_field_in_child_is_rejected() =>
        Assert.False(AppsConfigSerializer.Parse(Version2Example.Replace("\"check\": \"sha256\",", "\"check\": \"sha256\", \"args\": \"/x\",", StringComparison.Ordinal)).IsValid);
}
