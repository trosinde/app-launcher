using AppStart.Core;
using AppStart.Setup.Engine;

namespace AppStart.Setup.Tests.Engine;

public class AppCandidatesTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Hersteller\AppA\AppA.exe", "appa")]
    [InlineData(@"C:\x\AppStart.TestApp.exe", "appstart-testapp")]
    [InlineData(@"C:\x\Wäge Software_V2.exe", "wage-software-v2")]
    [InlineData(@"C:\x\--__--.exe", "app")]
    [InlineData(@"C:\x\Ein sehr langer Name einer Laborsoftware Version 12.exe", "ein-sehr-langer-name-einer-labor")]
    public void Id_from_file_name_is_valid(string exe, string expected)
    {
        var id = AppCandidates.SuggestId(exe, []);
        Assert.Equal(expected, id);
        Assert.True(AppId.IsValid(id));
    }

    [Fact]
    public void Id_is_unique_against_taken_ids()
    {
        Assert.Equal("appa-2", AppCandidates.SuggestId(@"C:\x\AppA.exe", ["appa"]));
        Assert.Equal("appa-3", AppCandidates.SuggestId(@"C:\y\AppA.exe", ["appa", "appa-2"]));
    }

    [Fact]
    public void Long_id_with_suffix_stays_within_32_characters()
    {
        var taken = AppCandidates.SuggestId(@"C:\x\Ein sehr langer Name einer Laborsoftware.exe", []);
        var second = AppCandidates.SuggestId(@"C:\y\Ein sehr langer Name einer Laborsoftware.exe", [taken]);
        Assert.True(second.Length <= 32);
        Assert.EndsWith("-2", second);
        Assert.True(AppId.IsValid(second));
    }
}
