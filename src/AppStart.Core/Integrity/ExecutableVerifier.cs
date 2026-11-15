using System.Security.Cryptography;
using AppStart.Core.Configuration;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace AppStart.Core.Integrity;

/// <summary>Outcome of an integrity check. <see cref="Detail"/> is for the event log only, never for the client.</summary>
public sealed record IntegrityResult(bool Success, string Detail)
{
    public static IntegrityResult Ok(string detail) => new(true, detail);

    public static IntegrityResult Fail(string detail) => new(false, detail);
}

/// <summary>Checks signature or SHA-256 of a locked EXE against apps.json (spec app-launch, design D7).</summary>
public static class ExecutableVerifier
{
    public static IntegrityResult Verify(AppEntry app, LockedExecutable exe) => app.Check switch
    {
        IntegrityCheck.Sha256 => VerifySha256(exe, app.Sha256!),
        IntegrityCheck.Signature => VerifySignature(exe, app.SignerThumbprint!),
        _ => IntegrityResult.Fail($"Unbekannte Prüfart {app.Check}."),
    };

    public static string ComputeSha256(LockedExecutable exe)
    {
        exe.Stream.Position = 0;
        var hash = SHA256.HashData(exe.Stream);
        return Convert.ToHexString(hash);
    }

    internal static IntegrityResult VerifySha256(LockedExecutable exe, string expected)
    {
        var actual = ComputeSha256(exe);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            ? IntegrityResult.Ok($"SHA-256 stimmt ({actual}).")
            : IntegrityResult.Fail($"SHA-256 weicht ab: erwartet {expected.ToUpperInvariant()}, gefunden {actual}.");
    }

    /// <summary>Valid Authenticode chain (no online revocation, design D7) and pinned signer thumbprint.</summary>
    internal static IntegrityResult VerifySignature(LockedExecutable exe, string expectedThumbprint)
    {
        var (trust, thumbprint) = WinVerifyTrustFile(exe);
        if (trust != 0)
        {
            return IntegrityResult.Fail($"Authenticode-Prüfung fehlgeschlagen (0x{trust:X8}).");
        }

        if (thumbprint is null)
        {
            return IntegrityResult.Fail("Signaturzertifikat nicht lesbar.");
        }

        return string.Equals(thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase)
            ? IntegrityResult.Ok($"Signatur gültig, Thumbprint {thumbprint}.")
            : IntegrityResult.Fail($"Signatur gültig, aber fremdes Zertifikat: erwartet {expectedThumbprint.ToUpperInvariant()}, gefunden {thumbprint}.");
    }

    /// <summary>Signer thumbprint of a validly signed file (for setup: add/rehash). Null when unsigned or invalid.</summary>
    public static string? TryGetValidSignerThumbprint(LockedExecutable exe)
    {
        var (trust, thumbprint) = WinVerifyTrustFile(exe);
        return trust == 0 ? thumbprint : null;
    }

    // Verifies the file through its locked handle and, while the trust state is open, reads the
    // SHA-1 thumbprint of the leaf certificate of exactly the signature that was verified.
    private static unsafe (int Result, string? Thumbprint) WinVerifyTrustFile(LockedExecutable exe)
    {
        fixed (char* path = exe.Path)
        {
            var file = new WINTRUST_FILE_INFO
            {
                cbStruct = (uint)sizeof(WINTRUST_FILE_INFO),
                pcwszFilePath = path,
                hFile = (HANDLE)exe.Stream.SafeFileHandle.DangerousGetHandle(),
            };

            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
                dwProvFlags = WINTRUST_DATA_PROVIDER_FLAGS.WTD_REVOCATION_CHECK_NONE
                              | WINTRUST_DATA_PROVIDER_FLAGS.WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            data.Anonymous.pFile = &file;

            var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
            var result = PInvoke.WinVerifyTrust(HWND.Null, &action, &data);
            string? thumbprint = null;
            try
            {
                if (result == 0)
                {
                    var provider = PInvoke.WTHelperProvDataFromStateData(data.hWVTStateData);
                    var signer = provider is null ? null : PInvoke.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                    if (signer is not null && signer->csCertChain > 0 && signer->pasCertChain[0].pCert is not null)
                    {
                        var cert = signer->pasCertChain[0].pCert;
                        var encoded = new ReadOnlySpan<byte>(cert->pbCertEncoded, (int)cert->cbCertEncoded);
                        thumbprint = Convert.ToHexString(SHA1.HashData(encoded));
                    }
                }
            }
            finally
            {
                data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
                PInvoke.WinVerifyTrust(HWND.Null, &action, &data);
            }

            return (result, thumbprint);
        }
    }
}
