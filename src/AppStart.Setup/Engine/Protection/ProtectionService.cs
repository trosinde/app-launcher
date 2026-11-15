using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using AppStart.Core;
using AppStart.Core.Security;
using Microsoft.Win32;

namespace AppStart.Setup.Engine.Protection;

/// <summary>
/// ACLs on C:\ProgramData\AppStart, apps.json, the data folders and registry keys of the apps (task 6.3,
/// spec installation "Rechte auf Daten und Registry", spec app-launch "Geschützte und gültige Konfiguration").
/// </summary>
/// <remarks>
/// <para>
/// Backup scope: <see cref="AclBackup"/> holds the descriptor (owner + DACL incl. protection flag) of the
/// top-level folder or key only. Children are reset to "inherit only" (explicit ACEs removed, owner set to
/// Administrators, like <c>icacls /reset /T</c> plus <c>/setowner</c>) and are NOT backed up. Reasons:
/// (1) in the usual case children only inherit anyway, so restoring the top level with inheritance from its
/// parent brings back the original effective rights for the whole tree; (2) a per-child backup would grow
/// with the data, would have to be matched against children the app created or deleted in the meantime and
/// would, when restored, hand the operators explicit access to data the app wrote while it was protected.
/// What is lost on uninstall: explicit ACEs that individual children had before setup, and their original
/// owners (children keep Administrators as owner).
/// </para>
/// <para>
/// Reparse points (junctions, symbolic links) below a data folder are neither changed nor followed, so a
/// planted link cannot redirect the ACL change to system files. The config folder and its fixed subfolders
/// must not be reparse points at all.
/// </para>
/// </remarks>
public sealed class ProtectionService : IProtectionService
{
    public const string ReportsFolderName = "Reports";
    public const string ShortcutBackupFolderName = "ShortcutBackup";

    private const string FileFullAccess = "FA";
    private const string KeyFullAccess = "KA";

    private static readonly SecurityIdentifier LocalSystem = WriteAccessPolicy.LocalSystem;
    private static readonly SecurityIdentifier Administrators = WriteAccessPolicy.Administrators;
    private static readonly SecurityIdentifier TrustedInstaller = WriteAccessPolicy.TrustedInstaller;

    private readonly string baseDirectory;
    private readonly SecurityIdentifier owner;
    private readonly SecurityIdentifier? additionalAdministrator;

    /// <param name="baseDirectory">Config folder, default <see cref="AppStartConstants.DataDirectory"/>.</param>
    public ProtectionService(string? baseDirectory = null)
        : this(baseDirectory ?? AppStartConstants.DataDirectory, Administrators, null)
    {
    }

    /// <summary>
    /// Tests: <paramref name="owner"/> replaces Administrators as owner and <paramref name="additionalAdministrator"/>
    /// gets the same rights as Administrators. A non-elevated test process can neither make Administrators the
    /// owner nor would it keep access (and could clean up) with a SYSTEM/Administrators-only ACL.
    /// </summary>
    internal ProtectionService(string baseDirectory, SecurityIdentifier owner, SecurityIdentifier? additionalAdministrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        this.baseDirectory = Path.GetFullPath(baseDirectory);
        this.owner = owner;
        this.additionalAdministrator = additionalAdministrator;
    }

    // ------------------------------------------------------------------ Config folder and files

    public void ProtectConfigDirectory()
    {
        OwnershipPrivileges.EnsureEnabled();

        foreach (var folder in new[]
                 {
                     baseDirectory,
                     Path.Combine(baseDirectory, ReportsFolderName),
                     Path.Combine(baseDirectory, ShortcutBackupFolderName),
                 })
        {
            Directory.CreateDirectory(folder);
            if (IsReparsePoint(folder))
            {
                throw new InvalidOperationException($"{folder} ist ein Verweis (Junction oder symbolischer Link) und wird nicht geschützt.");
            }
        }

        // SYSTEM and Administrators only; Reports, ShortcutBackup and any existing file just inherit.
        var dacl = "D:P" + TrustedAces(FileFullAccess, "OICI");
        ProtectTree(baseDirectory, dacl);
    }

    public void WriteProtectedFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        OwnershipPrivileges.EnsureEnabled();

        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target)
            ?? throw new ArgumentException($"{path} hat keinen Ordner.", nameof(path));
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"Ordner {folder} fehlt. Zuerst den Konfigurationsordner schützen.");
        }

        // The temp file gets the target ACL at creation (explicit, protected), so neither it nor the replaced
        // target is ever readable or writable for others; File.Move keeps the descriptor of the moved file.
        var security = new FileSecurity();
        security.SetSecurityDescriptorSddlForm($"O:{owner.Value}D:P" + TrustedAces(FileFullAccess, string.Empty));

        var temp = Path.Combine(folder, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileInfo(temp).Create(
                       FileMode.CreateNew,
                       FileSystemRights.Write | FileSystemRights.Synchronize,
                       FileShare.None,
                       4096,
                       FileOptions.None,
                       security))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    // ------------------------------------------------------------------ Data folders

    public AclBackup ProtectDataDirectory(string path, SecurityIdentifier appAccount, string appId)
    {
        ArgumentNullException.ThrowIfNull(appAccount);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var full = ValidateDataDirectory(path);
        OwnershipPrivileges.EnsureEnabled();

        Directory.CreateDirectory(full);
        if (IsReparsePoint(full))
        {
            throw new InvalidOperationException($"Datenordner {full} ist ein Verweis (Junction oder symbolischer Link) und wird nicht geschützt.");
        }

        var original = new DirectoryInfo(full)
            .GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
            .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);

        var dacl = "D:P" + Ace(appAccount, FileFullAccess, "OICI") + TrustedAces(FileFullAccess, "OICI", except: appAccount);
        ProtectTree(full, dacl);

        return new AclBackup(AclTarget.Directory, full, original, appId);
    }

    // ------------------------------------------------------------------ Registry keys

    public AclBackup ProtectRegistryKey(string key, SecurityIdentifier appAccount, string appId)
    {
        var (normalized, subKey) = RegistryPath.Parse(key);
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        return ProtectRegistryKey(root, subKey, normalized, appAccount, appId);
    }

    /// <summary>Tests: any root (HKCU); <paramref name="backupPath"/> is what the backup records.</summary>
    internal AclBackup ProtectRegistryKey(RegistryKey root, string subKey, string backupPath, SecurityIdentifier appAccount, string appId)
    {
        ArgumentNullException.ThrowIfNull(appAccount);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        OwnershipPrivileges.EnsureEnabled();

        using (var existing = root.OpenSubKey(subKey, RegistryKeyPermissionCheck.Default, RegistryRights.ReadPermissions))
        {
            if (existing is null)
            {
                root.CreateSubKey(subKey, RegistryKeyPermissionCheck.Default, RegistryOptions.None).Dispose();
            }
        }

        string original;
        using (var key = root.OpenSubKey(subKey, RegistryKeyPermissionCheck.Default, RegistryRights.ReadPermissions)
                         ?? throw new InvalidOperationException($"Registry-Key {backupPath} kann nicht geöffnet werden."))
        {
            original = key.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        }

        var dacl = "D:P" + Ace(appAccount, KeyFullAccess, "CI") + TrustedAces(KeyFullAccess, "CI", except: appAccount);
        ProtectKeyTree(root, subKey, dacl);

        return new AclBackup(AclTarget.RegistryKey, backupPath, original, appId);
    }

    // ------------------------------------------------------------------ Restore

    /// <remarks>
    /// Owner, explicit ACEs and the protection flag come back exactly; inherited ACEs are recomputed from the
    /// parent as it is today (SetSecurityInfo), which is what a later inheritance change should yield anyway.
    /// Windows then also marks the DACL as auto-inherited ("AI" in SDDL) even when the original lacked it;
    /// that flag has no effect on access.
    /// </remarks>
    public void Restore(AclBackup backup)
    {
        ArgumentNullException.ThrowIfNull(backup);
        switch (backup.Target)
        {
            case AclTarget.Directory:
                RestoreDirectory(backup);
                break;
            case AclTarget.RegistryKey:
                var (_, subKey) = RegistryPath.Parse(backup.Path);
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    RestoreRegistryKey(root, subKey, backup.OriginalSddl);
                }

                break;
            default:
                throw new ArgumentException($"Unbekanntes Ziel {backup.Target}.", nameof(backup));
        }
    }

    private void RestoreDirectory(AclBackup backup)
    {
        // Folder deleted in the meantime (e.g. by the app's uninstaller): nothing left to give back.
        if (!Directory.Exists(backup.Path))
        {
            return;
        }

        OwnershipPrivileges.EnsureEnabled();
        var info = new DirectoryInfo(backup.Path);

        // DACL first (we are owner, so WRITE_DAC is implicit), then the original owner (needs SeRestorePrivilege
        // when it is another account). Children stay "inherit only" and pick up the restored rights.
        var dacl = new DirectorySecurity();
        dacl.SetSecurityDescriptorSddlForm(backup.OriginalSddl, AccessControlSections.Access);
        info.SetAccessControl(dacl);

        var ownerOnly = new DirectorySecurity();
        ownerOnly.SetSecurityDescriptorSddlForm(backup.OriginalSddl, AccessControlSections.Owner);
        info.SetAccessControl(ownerOnly);
    }

    /// <summary>Tests: restore below any root.</summary>
    internal static void RestoreRegistryKey(RegistryKey root, string subKey, string originalSddl)
    {
        OwnershipPrivileges.EnsureEnabled();
        using var key = root.OpenSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership);
        if (key is null)
        {
            return;
        }

        var dacl = new RegistrySecurity();
        dacl.SetSecurityDescriptorSddlForm(originalSddl, AccessControlSections.Access);
        key.SetAccessControl(dacl);

        var ownerOnly = new RegistrySecurity();
        ownerOnly.SetSecurityDescriptorSddlForm(originalSddl, AccessControlSections.Owner);
        key.SetAccessControl(ownerOnly);
    }

    // ------------------------------------------------------------------ Verify

    public IReadOnlyList<string> FindForeignAccess(AclTarget target, string path, SecurityIdentifier appAccount)
    {
        ArgumentNullException.ThrowIfNull(appAccount);
        switch (target)
        {
            case AclTarget.Directory:
                if (!Directory.Exists(path))
                {
                    throw new DirectoryNotFoundException($"Datenordner {path} existiert nicht.");
                }

                var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                return FindForeignAccess(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0), appAccount, isRegistry: false);

            case AclTarget.RegistryKey:
                var (normalized, subKey) = RegistryPath.Parse(path);
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    return FindForeignAccess(root, subKey, normalized, appAccount);
                }

            default:
                throw new ArgumentException($"Unbekanntes Ziel {target}.", nameof(target));
        }
    }

    /// <summary>Tests: registry key below any root.</summary>
    internal IReadOnlyList<string> FindForeignAccess(RegistryKey root, string subKey, string displayPath, SecurityIdentifier appAccount)
    {
        using var key = root.OpenSubKey(subKey, RegistryKeyPermissionCheck.Default, RegistryRights.ReadPermissions)
                        ?? throw new InvalidOperationException($"Registry-Key {displayPath} existiert nicht.");
        var security = key.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        return FindForeignAccess(new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0), appAccount, isRegistry: true);
    }

    /// <summary>
    /// Every principal besides the app account, SYSTEM, Administrators and TrustedInstaller with an effective
    /// allow ACE, read access included ("für Bediener nicht lesbar"). A foreign owner counts too: the owner
    /// can always read and change the DACL.
    /// </summary>
    internal IReadOnlyList<string> FindForeignAccess(RawSecurityDescriptor descriptor, SecurityIdentifier appAccount, bool isRegistry)
    {
        var trusted = new HashSet<SecurityIdentifier> { appAccount, LocalSystem, Administrators, TrustedInstaller };
        if (additionalAdministrator is not null)
        {
            trusted.Add(additionalAdministrator);
        }

        var findings = new List<string>();
        if (descriptor.Owner is { } foreignOwner && !trusted.Contains(foreignOwner))
        {
            findings.Add($"{Describe(foreignOwner)}: Besitzer (darf Rechte lesen und ändern)");
        }

        if (descriptor.DiscretionaryAcl is null)
        {
            findings.Add("Jeder (NULL DACL): Vollzugriff");
            return findings;
        }

        foreach (var ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce known
                || known.AceQualifier != AceQualifier.AccessAllowed
                || (ace.AceFlags & AceFlags.InheritOnly) != 0
                || trusted.Contains(known.SecurityIdentifier))
            {
                continue;
            }

            findings.Add($"{Describe(known.SecurityIdentifier)}: {DescribeRights(known.AccessMask, isRegistry)}");
        }

        return findings;
    }

    internal static string DescribeRights(int mask, bool isRegistry)
    {
        const int genericAll = 0x10000000;
        const int genericRead = unchecked((int)0x80000000);
        const int genericWrite = 0x40000000;
        const int genericExecute = 0x20000000;
        const int delete = 0x10000;
        const int readControl = 0x20000;
        const int writeDac = 0x40000;
        const int writeOwner = 0x80000;

        var full = isRegistry ? (int)RegistryRights.FullControl : (int)FileSystemRights.FullControl;
        var hex = $"0x{mask:X8}";
        if ((mask & genericAll) != 0 || (mask & full) == full)
        {
            return $"Vollzugriff ({hex})";
        }

        // File: ReadData, ReadEA, Execute, ReadAttributes. Key: QueryValues, EnumerateSubKeys, Notify.
        var readBits = (isRegistry ? 0x1 | 0x8 | 0x10 : 0x1 | 0x8 | 0x20 | 0x80) | genericRead | genericExecute;

        // File: WriteData, AppendData, WriteEA, DeleteChild, WriteAttributes. Key: SetValue, CreateSubKey, CreateLink.
        var writeBits = (isRegistry ? 0x2 | 0x4 | 0x20 : 0x2 | 0x4 | 0x10 | 0x40 | 0x100) | genericWrite;

        var parts = new List<string>();
        if ((mask & readBits) != 0)
        {
            parts.Add("Lesen");
        }

        if ((mask & writeBits) != 0)
        {
            parts.Add("Schreiben");
        }

        if ((mask & delete) != 0)
        {
            parts.Add("Löschen");
        }

        if ((mask & writeDac) != 0)
        {
            parts.Add("Rechte ändern");
        }

        if ((mask & writeOwner) != 0)
        {
            parts.Add("Besitz übernehmen");
        }

        if (parts.Count == 0 && (mask & readControl) != 0)
        {
            parts.Add("Rechte lesen");
        }

        if (parts.Count == 0)
        {
            parts.Add("Sonstige");
        }

        return $"{string.Join(", ", parts)} ({hex})";
    }

    // ------------------------------------------------------------------ Helpers

    /// <summary>Protects <paramref name="root"/> with <paramref name="protectedDacl"/> and resets everything below.</summary>
    private void ProtectTree(string root, string protectedDacl)
    {
        var info = new DirectoryInfo(root);
        SetOwner(info);
        var dacl = new DirectorySecurity();
        dacl.SetSecurityDescriptorSddlForm(protectedDacl, AccessControlSections.Access);
        info.SetAccessControl(dacl);

        ResetChildren(info);
    }

    /// <summary>
    /// Like icacls /reset /T with /setowner: owner Administrators, explicit ACEs removed, inheritance on.
    /// Top-down, so a child that was closed to administrators becomes reachable once its parent is done.
    /// </summary>
    private void ResetChildren(DirectoryInfo folder)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            SetOwner(entry);
            if (entry is DirectoryInfo directory)
            {
                var inheritOnly = new DirectorySecurity();
                inheritOnly.SetSecurityDescriptorSddlForm("D:", AccessControlSections.Access);
                directory.SetAccessControl(inheritOnly);
                ResetChildren(directory);
            }
            else if (entry is FileInfo file)
            {
                var inheritOnly = new FileSecurity();
                inheritOnly.SetSecurityDescriptorSddlForm("D:", AccessControlSections.Access);
                file.SetAccessControl(inheritOnly);
            }
        }
    }

    private void SetOwner(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo directory)
        {
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm($"O:{owner.Value}", AccessControlSections.Owner);
            directory.SetAccessControl(security);
        }
        else if (entry is FileInfo file)
        {
            var security = new FileSecurity();
            security.SetSecurityDescriptorSddlForm($"O:{owner.Value}", AccessControlSections.Owner);
            file.SetAccessControl(security);
        }
    }

    private void ProtectKeyTree(RegistryKey root, string subKey, string sddl)
    {
        using var key = OpenForAcl(root, subKey);

        var ownerOnly = new RegistrySecurity();
        ownerOnly.SetSecurityDescriptorSddlForm($"O:{owner.Value}", AccessControlSections.Owner);
        key.SetAccessControl(ownerOnly);

        var dacl = new RegistrySecurity();
        dacl.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
        key.SetAccessControl(dacl);

        using var enumerable = root.OpenSubKey(subKey, RegistryKeyPermissionCheck.Default, RegistryRights.ReadKey)
                               ?? throw new InvalidOperationException($"Registry-Key {subKey} kann nicht geöffnet werden.");
        foreach (var name in enumerable.GetSubKeyNames())
        {
            ProtectKeyTree(enumerable, name, "D:");
        }
    }

    private static RegistryKey OpenForAcl(RegistryKey root, string subKey) =>
        root.OpenSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions | RegistryRights.TakeOwnership)
        ?? throw new InvalidOperationException($"Registry-Key {subKey} kann nicht geöffnet werden.");

    private string TrustedAces(string rights, string inheritance, SecurityIdentifier? except = null)
    {
        var builder = new StringBuilder();
        foreach (var sid in new[] { LocalSystem, Administrators, additionalAdministrator })
        {
            if (sid is not null && sid != except)
            {
                builder.Append(Ace(sid, rights, inheritance));
            }
        }

        return builder.ToString();
    }

    private static string Ace(SecurityIdentifier sid, string rights, string inheritance) =>
        $"(A;{inheritance};{rights};;;{sid.Value})";

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    /// <summary>Absolute folder that is neither a drive root nor a shared system folder.</summary>
    private string ValidateDataDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"Datenordner «{path}» muss ein absoluter Pfad sein.", nameof(path));
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty);

        var forbidden = new[]
            {
                Environment.SpecialFolder.Windows,
                Environment.SpecialFolder.System,
                Environment.SpecialFolder.SystemX86,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.CommonProgramFiles,
                Environment.SpecialFolder.CommonProgramFilesX86,
                Environment.SpecialFolder.CommonApplicationData,
                Environment.SpecialFolder.CommonDocuments,
                Environment.SpecialFolder.CommonDesktopDirectory,
                Environment.SpecialFolder.UserProfile,
            }
            .Select(Environment.GetFolderPath)
            .Where(p => p.Length > 0)
            .Select(p => Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)))
            .Append(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))) ?? string.Empty)
            .Append(Path.TrimEndingDirectorySeparator(baseDirectory))
            .Append(root);

        if (forbidden.Any(f => f.Length > 0 && string.Equals(f, full, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Datenordner «{full}» ist ein Laufwerk oder Systemordner und darf nicht gesperrt werden.", nameof(path));
        }

        return full;
    }

    private static string Describe(SecurityIdentifier sid)
    {
        try
        {
            return $"{sid.Translate(typeof(NTAccount))} ({sid.Value})";
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return $"Unbekannt ({sid.Value})";
        }
    }
}
