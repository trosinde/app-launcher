using System.Security.AccessControl;
using System.Security.Principal;

namespace AppStart.Core.Security;

/// <summary>
/// Checks that only SYSTEM, Administrators and TrustedInstaller can change a file or folder
/// (apps.json and C:\ProgramData\AppStart, spec app-launch; also used by verify).
/// </summary>
public static class WriteAccessPolicy
{
    public static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    public static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    public static readonly SecurityIdentifier TrustedInstaller = new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    // File: WriteData | AppendData. Folder: AddFile | AddSubdirectory | DeleteChild. Both: Delete, WRITE_DAC,
    // WRITE_OWNER, GENERIC_WRITE, GENERIC_ALL. Any of these lets the holder replace or rewrite the object.
    private const int FileWriteMask = 0x2 | 0x4;
    private const int FolderWriteMask = 0x2 | 0x4 | 0x40;
    private const int CommonWriteMask = 0x10000 | 0x40000 | 0x80000 | 0x40000000 | 0x10000000;

    private static readonly HashSet<SecurityIdentifier> Trusted = [LocalSystem, Administrators, TrustedInstaller];

    /// <summary>Returns one line per violation; empty when only trusted principals can write.</summary>
    public static IReadOnlyList<string> FindViolations(RawSecurityDescriptor descriptor, bool isDirectory)
    {
        var violations = new List<string>();

        if (descriptor.Owner is { } owner && !Trusted.Contains(owner))
        {
            violations.Add($"Besitzer {Describe(owner)} ist nicht SYSTEM, Administratoren oder TrustedInstaller.");
        }

        if (descriptor.DiscretionaryAcl is null)
        {
            violations.Add("Keine Zugriffsliste (NULL DACL): jeder hat Vollzugriff.");
            return violations;
        }

        var mask = CommonWriteMask | (isDirectory ? FolderWriteMask : FileWriteMask);
        foreach (var ace in descriptor.DiscretionaryAcl)
        {
            if (ace is not QualifiedAce known
                || known.AceQualifier != AceQualifier.AccessAllowed
                || (ace.AceFlags & AceFlags.InheritOnly) != 0
                || Trusted.Contains(known.SecurityIdentifier))
            {
                continue;
            }

            if ((known.AccessMask & mask) != 0)
            {
                violations.Add($"{Describe(known.SecurityIdentifier)} hat Schreibrechte (Maske 0x{known.AccessMask:X8}).");
            }
        }

        return violations;
    }

    /// <summary>Reads the descriptor of an existing file or folder and applies <see cref="FindViolations"/>.</summary>
    public static IReadOnlyList<string> FindViolations(string path)
    {
        var isDirectory = Directory.Exists(path);
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        return FindViolations(descriptor, isDirectory);
    }

    private static string Describe(SecurityIdentifier sid)
    {
        try
        {
            return $"{sid.Translate(typeof(NTAccount))} ({sid.Value})";
        }
        catch (Exception ex) when (ex is IdentityNotMappedException or SystemException)
        {
            return sid.Value;
        }
    }
}
