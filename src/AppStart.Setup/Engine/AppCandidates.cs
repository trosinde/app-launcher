using System.Diagnostics;
using System.Text;
using AppStart.Core;
using AppStart.Core.Configuration;
using AppStart.Core.Integrity;

namespace AppStart.Setup.Engine;

/// <summary>Turns an EXE into an <see cref="AppCandidate"/>: name, unique AppId and integrity check.</summary>
public static class AppCandidates
{
    /// <summary>
    /// AppId from the file name: lower case, everything outside a-z/0-9 becomes '-', no leading/trailing or
    /// double '-', at most 32 characters, unique against <paramref name="taken"/> (suffix -2, -3 ...).
    /// </summary>
    public static string SuggestId(string exePath, IEnumerable<string> taken)
    {
        var name = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in name.Normalize(NormalizationForm.FormD))
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(c);
            }
            else if (char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark
                     && sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-'); // "ä" -> "a" via FormD, "." / " " / "_" -> "-"
            }
        }

        var baseId = sb.ToString().Trim('-');
        if (baseId.Length == 0)
        {
            baseId = "app";
        }

        var used = new HashSet<string>(taken, StringComparer.Ordinal);
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? string.Empty : $"-{n}";
            var head = baseId.Length + suffix.Length > AppId.MaxLength ? baseId[..(AppId.MaxLength - suffix.Length)].TrimEnd('-') : baseId;
            var id = head + suffix;
            if (!used.Contains(id))
            {
                return id;
            }
        }
    }

    /// <summary>Display name: FileDescription, else ProductName, else the file name.</summary>
    public static string DisplayName(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
            {
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate.Trim();
                }
            }
        }
        catch (FileNotFoundException)
        {
        }

        return Path.GetFileNameWithoutExtension(exePath);
    }

    /// <summary>Reads the EXE once (locked): signature with thumbprint when validly signed, else SHA-256.</summary>
    public static AppCandidate Inspect(string exePath, IEnumerable<string> takenIds, AppEntry? existing = null)
    {
        var full = Path.GetFullPath(exePath);
        using var exe = LockedExecutable.Open(full);
        var thumbprint = ExecutableVerifier.TryGetValidSignerThumbprint(exe);
        var sha = thumbprint is null ? ExecutableVerifier.ComputeSha256(exe) : null;

        return new AppCandidate(
            ExePath: full,
            Name: existing?.Name ?? DisplayName(full),
            Id: existing?.Id ?? SuggestId(full, takenIds),
            Check: thumbprint is null ? IntegrityCheck.Sha256 : IntegrityCheck.Signature,
            SignerThumbprint: thumbprint,
            Sha256: sha,
            DataDir: existing?.DataDir,
            RegistryKey: existing?.RegistryKey,
            ChildProcesses: existing?.ChildProcessMode ?? ChildProcessMode.Blocked,
            AllowedChildren: existing?.AllowedChildren);
    }

    public static AppEntry ToEntry(AppCandidate c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Exe = c.ExePath,
        WorkingDir = Path.GetDirectoryName(c.ExePath)!,
        Check = c.Check,
        SignerThumbprint = c.Check == IntegrityCheck.Signature ? c.SignerThumbprint : null,
        Sha256 = c.Check == IntegrityCheck.Sha256 ? c.Sha256 : null,
        ChildProcesses = c.ChildProcesses,
        AllowedChildren = c.ChildProcesses == ChildProcessMode.Allowlist ? c.AllowedChildren : null,
        DataDir = string.IsNullOrWhiteSpace(c.DataDir) ? null : c.DataDir.Trim(),
        RegistryKey = string.IsNullOrWhiteSpace(c.RegistryKey) ? null : c.RegistryKey.Trim(),
    };
}
