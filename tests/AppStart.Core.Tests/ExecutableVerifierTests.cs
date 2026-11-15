using System.Security.Cryptography;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;

namespace AppStart.Core.Tests;

public sealed class ExecutableVerifierTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"appstart-{Guid.NewGuid():N}.exe");

    public ExecutableVerifierTests() => File.WriteAllBytes(_file, "MZ not really an exe"u8.ToArray());

    public void Dispose() => File.Delete(_file);

    private AppEntry Entry(IntegrityCheck check, string? sha = null, string? thumb = null) => new()
    {
        Id = "t",
        Name = "T",
        Exe = _file,
        WorkingDir = Path.GetTempPath(),
        Check = check,
        Sha256 = sha,
        SignerThumbprint = thumb,
    };

    [Fact]
    public void Sha256_match_and_mismatch()
    {
        var expected = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_file)));
        using var exe = LockedExecutable.Open(_file);

        Assert.Equal(expected, ExecutableVerifier.ComputeSha256(exe));
        Assert.True(ExecutableVerifier.Verify(Entry(IntegrityCheck.Sha256, sha: expected.ToLowerInvariant()), exe).Success);

        var wrong = ExecutableVerifier.Verify(Entry(IntegrityCheck.Sha256, sha: new string('0', 64)), exe);
        Assert.False(wrong.Success);
        Assert.Contains(expected, wrong.Detail, StringComparison.Ordinal);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Locked_file_cannot_be_written_renamed_or_deleted()
    {
        using var exe = LockedExecutable.Open(_file);

        Assert.Throws<IOException>(() => File.WriteAllText(_file, "swapped"));
        Assert.Throws<IOException>(() => File.Move(_file, _file + ".old"));
        Assert.Throws<IOException>(() => File.Delete(_file));
        // Reading stays possible (the loader needs it to start the process).
        Assert.NotEmpty(File.ReadAllBytes(_file));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Unsigned_file_fails_signature_check()
    {
        using var exe = LockedExecutable.Open(_file);
        var result = ExecutableVerifier.Verify(Entry(IntegrityCheck.Signature, thumb: new string('A', 40)), exe);

        Assert.False(result.Success);
        Assert.Null(ExecutableVerifier.TryGetValidSignerThumbprint(exe));
    }

    // System.Private.CoreLib.dll of the shared runtime carries an embedded Microsoft Authenticode signature.
    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Signed_file_passes_with_its_thumbprint_and_fails_with_another()
    {
        var signed = typeof(object).Assembly.Location;
        using var exe = LockedExecutable.Open(signed);

        var thumbprint = ExecutableVerifier.TryGetValidSignerThumbprint(exe);
        Assert.NotNull(thumbprint);
        Assert.Equal(40, thumbprint.Length);

        Assert.True(ExecutableVerifier.VerifySignature(exe, thumbprint.ToLowerInvariant()).Success);

        var foreign = ExecutableVerifier.VerifySignature(exe, new string('A', 40));
        Assert.False(foreign.Success);
        Assert.Contains("fremdes Zertifikat", foreign.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_cannot_be_opened() =>
        Assert.Throws<FileNotFoundException>(() => LockedExecutable.Open(_file + ".missing"));
}
