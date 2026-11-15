using System.Security.Principal;
using AppStart.Service.Pipe;

namespace AppStart.Service.Tests;

public class PipeAndEventTests
{
    [Fact]
    public void Event_ids_are_unique() =>
        Assert.Equal(EventIds.All.Count, EventIds.All.Select(e => e.Id).Distinct().Count());

    [Fact]
    public void Client_rights_exclude_create_pipe_instance()
    {
        var rights = Convert.ToInt32(PipeFactory.ClientRights, 16);
        const int FileCreatePipeInstance = 0x4;
        const int GenericWrite = 0x40000000;
        const int GenericAll = 0x10000000;
        Assert.Equal(0, rights & (FileCreatePipeInstance | GenericWrite | GenericAll));
        Assert.Equal(0x2, rights & 0x2);       // FILE_WRITE_DATA: send the request
        Assert.Equal(0x100, rights & 0x100);   // FILE_WRITE_ATTRIBUTES: set message read mode
    }

    [Fact(SkipUnless = nameof(IsWindows), Skip = "Braucht Windows (SecurityIdentifier).")]
    public void Sddl_matches_design_d9()
    {
        var group = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
        Assert.Equal("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;0x12018b;;;S-1-5-21-1-2-3-1001)(D;;GA;;;NU)", PipeFactory.BuildSddl(group));
    }

    public static bool IsWindows => OperatingSystem.IsWindows();
}
