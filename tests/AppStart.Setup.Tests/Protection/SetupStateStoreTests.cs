using System.IO;
using System.Security.AccessControl;
using AppStart.Setup.Engine;
using AppStart.Setup.Engine.Protection;

namespace AppStart.Setup.Tests.Protection;

public sealed class SetupStateStoreTests : IDisposable
{
    private readonly ProtectionTestScope scope = new();

    public void Dispose() => scope.Dispose();

    private SetupStateStore Store() => new(scope.Service, Path.Combine(scope.Root, "Config"));

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Missing_file_is_empty_state()
    {
        var state = Store().Load();

        Assert.Equal(1, state.Version);
        Assert.False(state.OperatorAccountCreatedBySetup);
        Assert.Empty(state.AclBackups);
    }

    [Fact(SkipUnless = nameof(TestPlatform.IsWindows), SkipType = typeof(TestPlatform), Skip = TestPlatform.WindowsOnly)]
    public void Roundtrip_keeps_everything_and_protects_the_file()
    {
        scope.Service.ProtectConfigDirectory();
        var state = new SetupState
        {
            OperatorAccountCreatedBySetup = true,
            AddedGroupMembers = [@"DOMAIN\Bediener"],
            AclBackups =
            [
                new AclBackup(AclTarget.Directory, @"C:\ProgramData\AppStartTest", "O:BAD:AI(A;OICIID;FA;;;SY)", "testapp"),
                new AclBackup(AclTarget.RegistryKey, @"HKLM\SOFTWARE\AppStartTest", "O:BAD:P(A;CI;KA;;;SY)", "testapp"),
            ],
            ShortcutBackups = [new ShortcutBackup(@"C:\Users\Public\Desktop\TestApp.lnk", @"C:\ProgramData\AppStart\ShortcutBackup\TestApp.lnk", "testapp")],
            CreatedShortcuts = [@"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\TestApp.lnk"],
        };
        var store = Store();

        store.Save(state);
        var loaded = store.Load();

        Assert.True(loaded.OperatorAccountCreatedBySetup);
        Assert.Equal(state.AddedGroupMembers, loaded.AddedGroupMembers);
        Assert.Equal(state.AclBackups, loaded.AclBackups);
        Assert.Equal(state.ShortcutBackups, loaded.ShortcutBackups);
        Assert.Equal(state.CreatedShortcuts, loaded.CreatedShortcuts);

        var json = File.ReadAllText(store.Path);
        Assert.Contains("\"aclBackups\"", json, StringComparison.Ordinal);
        Assert.Contains("\"target\": \"RegistryKey\"", json, StringComparison.Ordinal);
        Assert.Empty(scope.ForeignWriteViolations(store.Path));
        Assert.True((ProtectionTestScope.Descriptor(store.Path).ControlFlags & ControlFlags.DiscretionaryAclProtected) != 0);
    }

    [Theory]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[],\"shortcutBackups\":[],\"createdShortcuts\":[],\"extra\":1}")]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[],\"shortcutBackups\":[]}")]
    [InlineData("{\"version\":2,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[],\"shortcutBackups\":[],\"createdShortcuts\":[]}")]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":null,\"aclBackups\":[],\"shortcutBackups\":[],\"createdShortcuts\":[]}")]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[{\"target\":\"Directory\",\"path\":\"C:\\\\X\",\"appId\":\"a\"}],\"shortcutBackups\":[],\"createdShortcuts\":[]}")]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[{\"target\":\"Other\",\"path\":\"C:\\\\X\",\"originalSddl\":\"D:\",\"appId\":\"a\"}],\"shortcutBackups\":[],\"createdShortcuts\":[]}")]
    [InlineData("{\"version\":1,\"operatorAccountCreatedBySetup\":false,\"addedGroupMembers\":[],\"aclBackups\":[{\"target\":7,\"path\":\"C:\\\\X\",\"originalSddl\":\"D:\",\"appId\":\"a\"}],\"shortcutBackups\":[],\"createdShortcuts\":[]}")]
    [InlineData("null")]
    [InlineData("{")]
    public void Invalid_content_is_rejected(string json) =>
        Assert.Throws<InvalidDataException>(() => SetupStateStore.Parse(json));

    [Fact]
    public void Serialized_state_parses_again()
    {
        var json = SetupStateStore.Serialize(new SetupState());

        Assert.Equal(new SetupState().Version, SetupStateStore.Parse(json).Version);
    }
}
