using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using AppStart.Setup.Engine.Protection;

namespace AppStart.Setup.Tests.Protection;

/// <summary>
/// Own temp folder under %TEMP% and own key under HKCU\Software\AppStartTests\&lt;guid&gt;, both removed again.
/// The current user is owner, "app account" and stand-in administrator, so it keeps full control and can clean up.
/// </summary>
internal sealed class ProtectionTestScope : IDisposable
{
    public static readonly SecurityIdentifier Users = new(WellKnownSidType.BuiltinUsersSid, null);

    public ProtectionTestScope()
    {
        Me = WindowsIdentity.GetCurrent().User!;
        Root = Path.Combine(Path.GetTempPath(), "AppStartTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        RegistryBase = @"Software\AppStartTests\" + Guid.NewGuid().ToString("N");
        Service = new ProtectionService(Path.Combine(Root, "Config"), Me, Me);
    }

    public SecurityIdentifier Me { get; }

    public string Root { get; }

    public string RegistryBase { get; }

    public ProtectionService Service { get; }

    public static string Sddl(string path)
    {
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        return security.GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
    }

    /// <summary>
    /// SDDL without the DACL's auto-inherited flag ("D:AI" / "D:PAI"), which SetSecurityInfo always sets and which
    /// does not affect access (see ProtectionService.Restore).
    /// </summary>
    public static string WithoutAutoInherited(string sddl) =>
        sddl.Replace("D:PAI(", "D:P(", StringComparison.Ordinal).Replace("D:AI(", "D:(", StringComparison.Ordinal);

    public static RawSecurityDescriptor Descriptor(string path) => new(Sddl(path));

    public static void GrantUsersRead(string file)
    {
        var info = new FileInfo(file);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.Read, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    public static bool HasExplicitAces(RawSecurityDescriptor descriptor) =>
        descriptor.DiscretionaryAcl!.Cast<GenericAce>().Any(a => (a.AceFlags & AceFlags.Inherited) == 0);

    /// <summary>Violations of the service's write check, ignoring the current user (it plays the app account).</summary>
    public IReadOnlyList<string> ForeignWriteViolations(string path) =>
        AppStart.Core.Security.WriteAccessPolicy.FindViolations(path)
            .Where(v => !v.Contains(Me.Value, StringComparison.Ordinal))
            .ToList();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(RegistryBase, throwOnMissingSubKey: false);
        using var parent = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\AppStartTests", writable: true);
        if (parent is not null && parent.SubKeyCount == 0 && parent.ValueCount == 0)
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\AppStartTests", throwOnMissingSubKey: false);
        }
    }
}
