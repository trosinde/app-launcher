using AppStart.Core.Configuration;

namespace AppStart.Core.Tests;

// add-allowed-child-processes, task 2.4 (design D5): one test per validation field.
public class AnswerFileTests
{
    // Example from design.md D5.
    private const string Example = """
        {
          "version": 1,
          "apps": [{
            "exe": "C:\\Program Files\\Hersteller\\AppA\\AppA.exe",
            "id": "appa", "name": "Applikation A",
            "dataDir": "C:\\ProgramData\\Hersteller\\AppA",
            "registryKey": "HKLM\\SOFTWARE\\Hersteller\\AppA",
            "childProcesses": "allowlist",
            "allowedChildren": [ { "exe": "C:\\Program Files\\Hersteller\\AppA\\Export.exe" } ]
          }],
          "operators": [ "DOMAIN\\Bediener-Gruppe" ]
        }
        """;

    private static IReadOnlyList<string> Errors(string json) => AnswerFileSerializer.Parse(json).Errors;

    private static string Replace(string from, string to)
    {
        var json = Example.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(Example, json);
        return json;
    }

    [Fact]
    public void Design_example_is_valid()
    {
        var (file, errors) = AnswerFileSerializer.Parse(Example);

        Assert.Empty(errors);
        var app = Assert.Single(file!.Apps);
        Assert.Equal(ChildProcessMode.Allowlist, app.ChildProcesses);
        Assert.Null(app.Check);
        Assert.Null(app.AllowedChildren![0].Check);
        Assert.Equal(@"DOMAIN\Bediener-Gruppe", Assert.Single(file.Operators!));
    }

    [Fact]
    public void Minimal_file_needs_only_the_exe() =>
        Assert.Empty(Errors("""{ "version": 1, "apps": [ { "exe": "C:\\A\\A.exe" } ] }"""));

    [Theory]
    [InlineData("\"version\": 1", "\"version\": 2", "version")]
    [InlineData("\"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\AppA.exe\"", "\"exe\": \"AppA.exe\"", "apps[0].exe")]
    [InlineData("\"id\": \"appa\"", "\"id\": \"App A\"", "apps[0].id")]
    [InlineData("\"name\": \"Applikation A\"", "\"name\": \" \"", "apps[0].name")]
    [InlineData("\"dataDir\": \"C:\\\\ProgramData\\\\Hersteller\\\\AppA\"", "\"dataDir\": \"\\\\\\\\server\\\\share\"", "apps[0].dataDir")]
    [InlineData("\"registryKey\": \"HKLM\\\\SOFTWARE\\\\Hersteller\\\\AppA\"", "\"registryKey\": \"HKCU\\\\Software\"", "apps[0].registryKey")]
    [InlineData("{ \"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\Export.exe\" }", "{ \"exe\": \"%ProgramFiles%\\\\Export.exe\" }", "apps[0].allowedChildren[0].exe")]
    [InlineData("{ \"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\Export.exe\" }", "{ \"exe\": \"C:\\\\Windows\\\\System32\\\\cmd.exe\" }", "apps[0].allowedChildren[0].allowShell")]
    [InlineData("\"childProcesses\": \"allowlist\"", "\"childProcesses\": \"blocked\"", "apps[0].allowedChildren")]
    [InlineData("\"allowedChildren\": [ { \"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\Export.exe\" } ]", "\"allowedChildren\": []", "apps[0].allowedChildren")]
    [InlineData("[ \"DOMAIN\\\\Bediener-Gruppe\" ]", "[ \" \" ]", "operators[0]")]
    [InlineData("[ \"DOMAIN\\\\Bediener-Gruppe\" ]", "[ \"A\\\\B\\\\C\" ]", "operators[0]")]
    [InlineData("[ \"DOMAIN\\\\Bediener-Gruppe\" ]", "[ \"DOMAIN\\\\Bediener-Gruppe\", \"domain\\\\bediener-gruppe\" ]", "operators[1]")]
    public void Each_rule_names_its_field(string from, string to, string field)
    {
        var errors = Errors(Replace(from, to));
        Assert.Contains(errors, e => e.StartsWith(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Shell_with_allow_shell_is_accepted() =>
        Assert.Empty(Errors(Replace("{ \"exe\": \"C:\\\\Program Files\\\\Hersteller\\\\AppA\\\\Export.exe\" }",
            "{ \"exe\": \"C:\\\\Windows\\\\System32\\\\cmd.exe\", \"allowShell\": true }")));

    [Fact]
    public void Duplicate_apps_and_children_are_reported()
    {
        const string json = """
            { "version": 1, "apps": [
              { "exe": "C:\\A\\A.exe", "id": "a", "childProcesses": "allowlist",
                "allowedChildren": [ { "exe": "C:\\A\\E.exe" }, { "exe": "c:\\a\\e.EXE" } ] },
              { "exe": "c:\\a\\a.exe", "id": "a" } ] }
            """;
        var errors = Errors(json);

        Assert.Contains(errors, e => e.StartsWith("apps[0].allowedChildren[1].exe", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("apps[1].exe", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.StartsWith("apps[1].id", StringComparison.Ordinal));
    }

    [Fact]
    public void All_errors_are_reported_at_once()
    {
        var errors = Errors("""{ "version": 1, "apps": [ { "exe": "x" }, { "exe": "y", "registryKey": "HKCU\\X" } ] }""");
        Assert.Equal(3, errors.Count);
    }

    [Theory]
    [InlineData("""{ "version": 1, "apps": [], "password": "x" }""")]                   // unknown field (no passwords)
    [InlineData("""{ "version": 1, "apps": [ { "exe": "C:\\A\\A.exe", "sha256": "00" } ] }""")] // check values not in the file
    [InlineData("""{ "version": 1, "apps": [], }""")]                                   // trailing comma
    [InlineData("{ // comment\n \"version\": 1, \"apps\": [] }")]                      // comment
    [InlineData("""{ "apps": [] }""")]                                                  // version missing
    [InlineData("""{ "version": 1, "apps": [ { "id": "a" } ] }""")]                     // exe missing
    [InlineData("""{ "version": 1, "apps": [ { "exe": "C:\\A\\A.exe", "check": "md5" } ] }""")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Malformed_files_are_rejected(string json)
    {
        var (file, errors) = AnswerFileSerializer.Parse(json);
        Assert.Null(file);
        Assert.NotEmpty(errors);
    }
}
