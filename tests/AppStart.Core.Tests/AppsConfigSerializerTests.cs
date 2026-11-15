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
              "name": "Applikation A",
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
    [InlineData("\"version\": 1", "\"version\": 2", "version")]
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

        Assert.Equal("apps[1].id: «app-a» kommt mehrfach vor.", error);
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
}
