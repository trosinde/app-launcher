using AppStart.Core.Configuration;
using AppStart.Core.Integrity;
using AppStart.Core.Security;

namespace AppStart.Setup.Engine;

/// <summary>Invalid input of a setup operation (command line exit code 2); nothing was changed.</summary>
public sealed class SetupInputException(string message) : Exception(message);

/// <summary>Helper programs of an application (add-allowed-child-processes, design D1, D3, D4).</summary>
public static class ChildPrograms
{
    /// <summary>Default check of an operator-writable folder or file (non-admins may write): violations, empty when protected.</summary>
    public static IReadOnlyList<string> WriteViolations(string exePath)
    {
        var folder = Path.GetDirectoryName(exePath)!;
        return [.. WriteAccessPolicy.FindViolations(folder), .. WriteAccessPolicy.FindViolations(exePath)];
    }

    /// <summary>
    /// Reads a helper EXE (locked) and builds its allowlist entry: signature when validly signed, else SHA-256,
    /// unless <paramref name="check"/> asks for one. Throws <see cref="SetupInputException"/> for invalid input
    /// (no absolute EXE path, shell without confirmation, signature requested for an unsigned file).
    /// </summary>
    public static AllowedChild Inspect(string exePath, IntegrityCheck? check, bool allowShell)
    {
        var full = Path.GetFullPath(exePath);
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new SetupInputException(string.Format(SetupEngineTexts.ChildNotExe, exePath));
        }

        if (ChildProcessRules.IsShellOrScriptHost(full) && !allowShell)
        {
            throw new SetupInputException(string.Format(SetupEngineTexts.ChildIsShell, Path.GetFileName(full)));
        }

        if (!File.Exists(full))
        {
            throw new SetupInputException(string.Format(SetupEngineTexts.ChildMissing, full));
        }

        using var exe = LockedExecutable.Open(full);
        var thumbprint = ExecutableVerifier.TryGetValidSignerThumbprint(exe);
        var use = check ?? (thumbprint is null ? IntegrityCheck.Sha256 : IntegrityCheck.Signature);
        if (use == IntegrityCheck.Signature && thumbprint is null)
        {
            throw new SetupInputException(string.Format(SetupEngineTexts.ChildNotSigned, full));
        }

        return use == IntegrityCheck.Signature
            ? new AllowedChild { Exe = full, Check = IntegrityCheck.Signature, SignerThumbprint = thumbprint, AllowShell = allowShell && ChildProcessRules.IsShellOrScriptHost(full) }
            : new AllowedChild { Exe = full, Check = IntegrityCheck.Sha256, Sha256 = ExecutableVerifier.ComputeSha256(exe), AllowShell = allowShell && ChildProcessRules.IsShellOrScriptHost(full) };
    }

    /// <summary>A fresh SHA-256 for an entry with check sha256 (rehash); signature entries stay as they are.</summary>
    public static AllowedChild Rehash(AllowedChild entry)
    {
        if (entry.Check != IntegrityCheck.Sha256)
        {
            return entry;
        }

        using var exe = LockedExecutable.Open(entry.Exe);
        return entry with { Sha256 = ExecutableVerifier.ComputeSha256(exe) };
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
