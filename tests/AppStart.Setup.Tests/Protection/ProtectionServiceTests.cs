using System.IO;
using System.Security.AccessControl;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Protection;
using Microsoft.Win32;

namespace AppStart.Setup.Tests.Protection;

public sealed class ProtectionServiceTests : IDisposable
{
    private readonly ProtectionTestScope scope = new();

    public void Dispose() => scope.Dispose();

    // ------------------------------------------------------------------ Data folder

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectDataDirectory_leaves_no_access_for_others()
    {
        var data = Path.Combine(scope.Root, "Data");
        Directory.CreateDirectory(Path.Combine(data, "Sub"));
        var file = Path.Combine(data, "Sub", "secret.txt");
        File.WriteAllText(file, "x");

        var backup = scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");

        Assert.Equal(AclTarget.Directory, backup.Target);
        Assert.Equal("app-a", backup.AppId);
        Assert.Empty(scope.ForeignWriteViolations(data));
        Assert.Empty(scope.ForeignWriteViolations(file));
        Assert.Empty(scope.Service.FindForeignAccess(AclTarget.Directory, data, scope.Me));

        var top = ProtectionTestScope.Descriptor(data);
        Assert.True((top.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
        Assert.Equal(scope.Me, top.Owner);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectDataDirectory_creates_missing_folder()
    {
        var data = Path.Combine(scope.Root, "New", "Data");

        scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");

        Assert.True(Directory.Exists(data));
        Assert.Empty(scope.Service.FindForeignAccess(AclTarget.Directory, data, scope.Me));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectDataDirectory_removes_explicit_ace_of_child()
    {
        var data = Path.Combine(scope.Root, "Data");
        Directory.CreateDirectory(data);
        var file = Path.Combine(data, "passwords.txt");
        File.WriteAllText(file, "x");
        ProtectionTestScope.GrantUsersRead(file);
        Assert.True(ProtectionTestScope.HasExplicitAces(ProtectionTestScope.Descriptor(file)));

        scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");

        var child = ProtectionTestScope.Descriptor(file);
        Assert.False(ProtectionTestScope.HasExplicitAces(child));
        Assert.DoesNotContain(child.DiscretionaryAcl!.Cast<GenericAce>(), a => a is CommonAce c && c.SecurityIdentifier == ProtectionTestScope.Users);
        Assert.Equal("x", File.ReadAllText(file));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Restore_puts_back_the_original_descriptor_exactly()
    {
        var data = Path.Combine(scope.Root, "Data");
        Directory.CreateDirectory(data);
        var original = ProtectionTestScope.Sddl(data);

        var backup = scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");
        Assert.Equal(original, backup.OriginalSddl);
        Assert.NotEqual(original, ProtectionTestScope.Sddl(data));

        scope.Service.Restore(backup);

        Assert.Equal(ProtectionTestScope.WithoutAutoInherited(original), ProtectionTestScope.WithoutAutoInherited(ProtectionTestScope.Sddl(data)));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Restore_puts_back_a_protected_original_with_explicit_aces()
    {
        var data = Path.Combine(scope.Root, "Data");
        Directory.CreateDirectory(data);
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"D:PAI(A;OICI;FA;;;{scope.Me.Value})(A;OICI;0x1200a9;;;BU)", AccessControlSections.Access);
        new DirectoryInfo(data).SetAccessControl(security);
        var original = ProtectionTestScope.Sddl(data);

        var backup = scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");
        scope.Service.Restore(backup);

        Assert.Equal(original, ProtectionTestScope.Sddl(data));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Restore_of_deleted_folder_does_nothing()
    {
        var data = Path.Combine(scope.Root, "Data");
        var backup = scope.Service.ProtectDataDirectory(data, scope.Me, "app-a");
        Directory.Delete(data);

        scope.Service.Restore(backup);

        Assert.False(Directory.Exists(data));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void FindForeignAccess_reports_users_read()
    {
        var data = Path.Combine(scope.Root, "Data");
        Directory.CreateDirectory(data);
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"D:P(A;OICI;FA;;;{scope.Me.Value})(A;OICI;FR;;;BU)", AccessControlSections.Access);
        new DirectoryInfo(data).SetAccessControl(security);

        var finding = Assert.Single(scope.Service.FindForeignAccess(AclTarget.Directory, data, scope.Me));

        Assert.Contains("(S-1-5-32-545)", finding, StringComparison.Ordinal);
        Assert.Contains("Lesen", finding, StringComparison.Ordinal);
        Assert.DoesNotContain("Schreiben", finding, StringComparison.Ordinal);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void FindForeignAccess_ignores_trusted_and_inherit_only()
    {
        var descriptor = new RawSecurityDescriptor(
            "O:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;OICIIO;GA;;;CO)(A;;FA;;;S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464)");

        Assert.Empty(scope.Service.FindForeignAccess(descriptor, scope.Me, isRegistry: false));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void FindForeignAccess_reports_foreign_owner_and_null_dacl()
    {
        var findings = scope.Service.FindForeignAccess(new RawSecurityDescriptor("O:BUD:NO_ACCESS_CONTROL"), scope.Me, isRegistry: false);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.Contains("Besitzer", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("NULL DACL", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("C:\\")]
    [InlineData("relative\\data")]
    public void ProtectDataDirectory_rejects_roots_and_relative_paths(string path) =>
        Assert.Throws<ArgumentException>(() => scope.Service.ProtectDataDirectory(path, scope.Me, "app-a"));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectDataDirectory_rejects_system_folders()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.Windows, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.CommonApplicationData })
        {
            Assert.Throws<ArgumentException>(() => scope.Service.ProtectDataDirectory(Environment.GetFolderPath(folder), scope.Me, "app-a"));
        }
    }

    // ------------------------------------------------------------------ Config folder and files

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectConfigDirectory_creates_and_protects_folders()
    {
        var config = Path.Combine(scope.Root, "Config");
        Directory.CreateDirectory(config);
        var stray = Path.Combine(config, "old.txt");
        File.WriteAllText(stray, "x");
        ProtectionTestScope.GrantUsersRead(stray);

        scope.Service.ProtectConfigDirectory();

        foreach (var path in new[] { config, Path.Combine(config, "Reports"), Path.Combine(config, "ShortcutBackup"), stray })
        {
            Assert.True(Path.Exists(path));
            Assert.Empty(scope.ForeignWriteViolations(path));
            Assert.DoesNotContain(
                ProtectionTestScope.Descriptor(path).DiscretionaryAcl!.Cast<GenericAce>(),
                a => a is CommonAce c && c.SecurityIdentifier == ProtectionTestScope.Users);
        }

        Assert.True((ProtectionTestScope.Descriptor(config).ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
        Assert.Empty(scope.Service.FindForeignAccess(AclTarget.Directory, config, scope.Me));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void WriteProtectedFile_writes_atomically_and_overwrites()
    {
        scope.Service.ProtectConfigDirectory();
        var config = Path.Combine(scope.Root, "Config");
        var file = Path.Combine(config, "apps.json");

        scope.Service.WriteProtectedFile(file, "{\"version\":1}");
        Assert.Equal("{\"version\":1}", File.ReadAllText(file));

        // An existing, readable-for-users file is replaced together with its ACL.
        ProtectionTestScope.GrantUsersRead(file);
        scope.Service.WriteProtectedFile(file, "{\"version\":2}");

        Assert.Equal("{\"version\":2}", File.ReadAllText(file));
        var descriptor = ProtectionTestScope.Descriptor(file);
        Assert.True((descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
        Assert.DoesNotContain(descriptor.DiscretionaryAcl!.Cast<GenericAce>(), a => a is CommonAce c && c.SecurityIdentifier == ProtectionTestScope.Users);
        Assert.Empty(scope.ForeignWriteViolations(file));
        Assert.Equal(["apps.json"], Directory.GetFiles(config).Select(Path.GetFileName));
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void WriteProtectedFile_without_folder_fails()
    {
        var file = Path.Combine(scope.Root, "Missing", "apps.json");

        Assert.Throws<DirectoryNotFoundException>(() => scope.Service.WriteProtectedFile(file, "{}"));
        Assert.False(File.Exists(file));
    }

    // ------------------------------------------------------------------ Registry (HKCU stand-in for HKLM)

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectRegistryKey_protects_resets_children_and_restores()
    {
        var subKey = scope.RegistryBase + @"\App";
        using (var key = Registry.CurrentUser.CreateSubKey(subKey + @"\Child"))
        {
            key.SetValue("Password", "geheim");
            var security = key.GetAccessControl();
            security.AddAccessRule(new RegistryAccessRule(ProtectionTestScope.Users, RegistryRights.ReadKey, AccessControlType.Allow));
            key.SetAccessControl(security);
        }

        string original;
        using (var key = Registry.CurrentUser.OpenSubKey(subKey)!)
        {
            original = key.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
        }

        var backup = scope.Service.ProtectRegistryKey(Registry.CurrentUser, subKey, "HKCU\\" + subKey, scope.Me, "app-a");

        Assert.Equal(AclTarget.RegistryKey, backup.Target);
        Assert.Equal(original, backup.OriginalSddl);
        Assert.Empty(scope.Service.FindForeignAccess(Registry.CurrentUser, subKey, subKey, scope.Me));
        Assert.Empty(scope.Service.FindForeignAccess(Registry.CurrentUser, subKey + @"\Child", subKey, scope.Me));
        using (var child = Registry.CurrentUser.OpenSubKey(subKey + @"\Child")!)
        {
            var descriptor = new RawSecurityDescriptor(child.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.False(ProtectionTestScope.HasExplicitAces(descriptor));
            Assert.Equal("geheim", child.GetValue("Password"));
        }

        using (var key = Registry.CurrentUser.OpenSubKey(subKey)!)
        {
            var descriptor = new RawSecurityDescriptor(key.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.True((descriptor.ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
        }

        ProtectionService.RestoreRegistryKey(Registry.CurrentUser, subKey, backup.OriginalSddl);

        using (var key = Registry.CurrentUser.OpenSubKey(subKey)!)
        {
            Assert.Equal(
                ProtectionTestScope.WithoutAutoInherited(original),
                ProtectionTestScope.WithoutAutoInherited(key.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access)));
        }
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void ProtectRegistryKey_creates_missing_key()
    {
        var subKey = scope.RegistryBase + @"\New\App";

        scope.Service.ProtectRegistryKey(Registry.CurrentUser, subKey, "HKCU\\" + subKey, scope.Me, "app-a");

        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        Assert.NotNull(key);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void FindForeignAccess_reports_users_read_on_key()
    {
        var subKey = scope.RegistryBase + @"\Open";
        using (var key = Registry.CurrentUser.CreateSubKey(subKey))
        {
            var security = new RegistrySecurity();
            security.SetSecurityDescriptorSddlForm($"D:P(A;CI;KA;;;{scope.Me.Value})(A;CI;KR;;;BU)", AccessControlSections.Access);
            key.SetAccessControl(security);
        }

        var finding = Assert.Single(scope.Service.FindForeignAccess(Registry.CurrentUser, subKey, subKey, scope.Me));
        Assert.Contains("(S-1-5-32-545): Lesen", finding, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"HKCU\SOFTWARE\Vendor\App")]
    [InlineData(@"HKEY_CURRENT_USER\Software\Vendor")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services")]
    [InlineData(@"HKLM\SOFTWARE")]
    [InlineData(@"HKLM\SOFTWARE\")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft")]
    [InlineData(@"HKLM\SOFTWARE\Vendor\\App")]
    [InlineData(@"SOFTWARE\Vendor")]
    public void Registry_path_outside_hklm_software_is_rejected(string key) =>
        Assert.Throws<ArgumentException>(() => RegistryPath.Parse(key));

    [Theory]
    [InlineData(@"HKLM\SOFTWARE\Hersteller\AppA", @"SOFTWARE\Hersteller\AppA")]
    [InlineData(@"HKEY_LOCAL_MACHINE\Software\AppStartTest", @"Software\AppStartTest")]
    [InlineData(@"hklm\SOFTWARE\Microsoft\Vendor", @"SOFTWARE\Microsoft\Vendor")]
    public void Registry_path_under_hklm_software_is_parsed(string key, string expectedSubKey)
    {
        var (normalized, subKey) = RegistryPath.Parse(key);
        Assert.Equal(expectedSubKey, subKey);
        Assert.Equal(@"HKLM\" + expectedSubKey, normalized);
    }

    [Fact]
    public void Rights_are_described_readably()
    {
        Assert.StartsWith("Vollzugriff", ProtectionService.DescribeRights((int)FileSystemRights.FullControl, isRegistry: false), StringComparison.Ordinal);
        Assert.StartsWith("Lesen (", ProtectionService.DescribeRights((int)FileSystemRights.ReadAndExecute, isRegistry: false), StringComparison.Ordinal);
        Assert.StartsWith("Lesen, Schreiben, Löschen", ProtectionService.DescribeRights((int)FileSystemRights.Modify, isRegistry: false), StringComparison.Ordinal);
        Assert.StartsWith("Lesen (", ProtectionService.DescribeRights((int)RegistryRights.ReadKey, isRegistry: true), StringComparison.Ordinal);
    }
}
