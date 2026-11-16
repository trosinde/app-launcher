using AppStart.Core.Configuration;
using AppStart.Core.Learning;

namespace AppStart.Core.Tests;

// add-allowed-child-processes, task 2.3 (design D3).
public class LearningTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
    private const string Thumb = "0123456789ABCDEF0123456789ABCDEF01234567";
    private const string Sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";
    private const string Export = @"C:\Program Files\Hersteller\AppA\Export.exe";
    private const string AppA = @"C:\Program Files\Hersteller\AppA\AppA.exe";

    // ------------------------------------------------ learn.json

    [Fact]
    public void Start_uses_the_given_duration_and_ends_on_time()
    {
        var state = LearnStateSerializer.Empty.Start("appa", LearnState.DefaultMinutes, T0);

        Assert.NotNull(state.ActiveFor("appa", T0));
        Assert.NotNull(state.ActiveFor("appa", T0.AddMinutes(29)));
        Assert.Null(state.ActiveFor("appa", T0.AddMinutes(30)));   // expired: allowlist applies again
        Assert.Null(state.ActiveFor("appb", T0));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void Duration_outside_5_to_120_minutes_is_refused(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LearnStateSerializer.Empty.Start("appa", minutes, T0));

    [Fact]
    public void Several_apps_learn_independently_and_restart_replaces()
    {
        var state = LearnStateSerializer.Empty.Start("appa", 30, T0).Start("appb", 10, T0).Start("appa", 60, T0.AddMinutes(5));

        Assert.Equal(2, state.Apps.Count);
        Assert.Equal(T0.AddMinutes(65), state.ActiveFor("appa", T0.AddMinutes(6))!.ExpiresUtc);
        Assert.Null(state.Stop("appb").ActiveFor("appb", T0));
        Assert.NotNull(state.Stop("appb").ActiveFor("appa", T0.AddMinutes(6)));
    }

    [Fact]
    public void State_roundtrips()
    {
        var state = LearnStateSerializer.Empty.Start("appa", 30, T0);
        var (again, error) = LearnStateSerializer.Parse(LearnStateSerializer.Serialize(state));

        Assert.Null(error);
        Assert.Equal(state.Apps[0], again!.Apps[0]);
    }

    [Theory]
    [InlineData("{ \"version\": 2, \"apps\": [] }", "version")]
    [InlineData("{ \"version\": 1, \"apps\": [ { \"appId\": \"AppA\", \"startedUtc\": \"2026-11-15T10:00:00Z\", \"expiresUtc\": \"2026-11-15T10:30:00Z\" } ] }", "apps[0].appId")]
    [InlineData("{ \"version\": 1, \"apps\": [ { \"appId\": \"appa\", \"startedUtc\": \"2026-11-15T10:00:00Z\", \"expiresUtc\": \"2026-11-15T23:00:00Z\" } ] }", "apps[0].expiresUtc")]
    [InlineData("{ \"version\": 1, \"apps\": [ { \"appId\": \"appa\", \"startedUtc\": \"2026-11-15T10:00:00Z\", \"expiresUtc\": \"2026-11-15T10:30:00Z\" }, { \"appId\": \"appa\", \"startedUtc\": \"2026-11-15T10:00:00Z\", \"expiresUtc\": \"2026-11-15T10:30:00Z\" } ] }", "apps[1].appId")]
    public void Invalid_state_names_the_field(string json, string field)
    {
        var (state, error) = LearnStateSerializer.Parse(json);
        Assert.Null(state);
        Assert.StartsWith(field, error);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"apps\": [], \"extra\": 1 }")]
    [InlineData("{ \"version\": 1, \"apps\": [], }")]
    [InlineData("not json")]
    public void Malformed_state_is_rejected(string json) =>
        Assert.Null(LearnStateSerializer.Parse(json).State);

    [Fact]
    public void Missing_file_means_no_learn_mode_without_error()
    {
        var result = LearnStateStore.Load(Path.Combine(Path.GetTempPath(), $"appstart-{Guid.NewGuid():N}", "learn.json"));

        Assert.True(result.IsValid);
        Assert.False(result.Unprotected);
        Assert.Empty(result.State.Apps);
    }

    // Spec app-launch, scenario «Manipulierte Steuerdatei»: a file operators can write is ignored.
    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Writable_learn_file_is_ignored_and_reported()
    {
        var dir = Directory.CreateTempSubdirectory("appstart-").FullName;
        try
        {
            var path = Path.Combine(dir, "learn.json");
            File.WriteAllText(path, LearnStateSerializer.Serialize(LearnStateSerializer.Empty.Start("appa", 30, DateTimeOffset.UtcNow)));

            var result = LearnStateStore.Load(path);

            Assert.True(result.Unprotected);
            Assert.False(result.IsValid);
            Assert.Null(result.State.ActiveFor("appa", DateTimeOffset.UtcNow)); // fail-closed: allowlist applies
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ------------------------------------------------ Learn\<AppId>.json

    [Fact]
    public void Recording_counts_starts_and_keeps_first_and_last()
    {
        var rec = new LearnRecording { AppId = "appa" }
            .Record(Export, Thumb, Sha, AppA, T0)
            .Record(Export.ToUpperInvariant(), Thumb, Sha, AppA, T0.AddMinutes(3))
            .Record(Export, Thumb, Sha, @"C:\Program Files\Hersteller\AppA\Updater.exe", T0.AddMinutes(5));

        var p = Assert.Single(rec.Programs);
        Assert.Equal(3, p.Starts);
        Assert.Equal(T0, p.FirstStartUtc);
        Assert.Equal(T0.AddMinutes(5), p.LastStartUtc);
        Assert.Equal(2, p.ParentExes.Count);
    }

    [Fact]
    public void Signed_program_proposes_signature_unsigned_proposes_sha256()
    {
        var rec = new LearnRecording { AppId = "appa" }
            .Record(Export, Thumb, Sha, AppA, T0)
            .Record(@"C:\Program Files\Hersteller\AppA\Updater.exe", null, Sha, AppA, T0);

        var signed = rec.Programs.Single(p => p.Exe == Export).ToAllowedChild();
        Assert.Equal(IntegrityCheck.Signature, signed.Check);
        Assert.Equal(Thumb, signed.SignerThumbprint);

        var unsigned = rec.Programs.Single(p => p.Exe != Export).ToAllowedChild();
        Assert.Equal(IntegrityCheck.Sha256, unsigned.Check);
        Assert.Equal(Sha, unsigned.Sha256);
    }

    [Fact]
    public void Shell_is_recorded_but_marked()
    {
        var rec = new LearnRecording { AppId = "appa" }.Record(@"C:\Windows\System32\cmd.exe", Thumb, Sha, AppA, T0);

        Assert.True(Assert.Single(rec.Programs).IsShellOrScriptHost);
    }

    [Fact]
    public void Proposals_are_valid_allowlist_entries()
    {
        var rec = new LearnRecording { AppId = "appa" }.Record(Export, Thumb, Sha, AppA, T0);
        var app = new AppEntry
        {
            Id = "appa", Name = "A", Exe = AppA, WorkingDir = @"C:\Program Files\Hersteller\AppA",
            Check = IntegrityCheck.Signature, SignerThumbprint = Thumb,
            ChildProcesses = ChildProcessMode.Allowlist,
            AllowedChildren = [.. rec.Programs.Select(p => p.ToAllowedChild())],
        };

        Assert.Null(AppsConfigSerializer.Validate(new AppsConfig { Version = 2, Apps = [app] }));
    }

    [Fact]
    public void Recording_roundtrips_and_validates()
    {
        var rec = new LearnRecording { AppId = "appa" }.Record(Export, Thumb, Sha, AppA, T0).Record(AppA + ".x.exe", null, Sha, null, T0);
        var (again, error) = LearnRecordingSerializer.Parse(LearnRecordingSerializer.Serialize(rec));

        Assert.Null(error);
        Assert.Equal(2, again!.Programs.Count);
        Assert.Equal(rec.Programs[0].ParentExes, again.Programs[0].ParentExes);
    }

    [Fact]
    public void Inconsistent_signature_fields_are_rejected()
    {
        var bad = new LearnRecording { AppId = "appa" }.Record(Export, null, Sha, AppA, T0);
        bad = bad with { Programs = [bad.Programs[0] with { SignatureValid = true }] };

        Assert.StartsWith("programs[0].signerThumbprint", LearnRecordingSerializer.Validate(bad));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Writable_recording_is_refused()
    {
        var dir = Directory.CreateTempSubdirectory("appstart-").FullName;
        try
        {
            var rec = new LearnRecording { AppId = "appa" }.Record(Export, Thumb, Sha, AppA, T0);
            File.WriteAllText(LearnRecordingStore.PathFor("appa", dir), LearnRecordingSerializer.Serialize(rec));

            var (loaded, error) = LearnRecordingStore.Load("appa", dir);

            Assert.Null(loaded);
            Assert.Contains("nicht geschützt", error, StringComparison.Ordinal);
            Assert.Equal((null, null), LearnRecordingStore.Load("appb", dir)); // nothing recorded
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
