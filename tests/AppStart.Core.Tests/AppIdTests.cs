namespace AppStart.Core.Tests;

public class AppIdTests
{
    [Theory]
    [InlineData("app-a")]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("testapp-2")]
    [InlineData("abcdefghijklmnopqrstuvwxyz012345")] // 32
    public void Valid(string id) => Assert.True(AppId.IsValid(id));

    [Theory]
    [InlineData("")]
    [InlineData("App-A")]
    [InlineData("app_a")]
    [InlineData("app a")]
    [InlineData("app.a")]
    [InlineData("äpp")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")] // 33
    [InlineData("app\0")]
    [InlineData("..\\cmd")]
    public void Invalid(string id) => Assert.False(AppId.IsValid(id));
}
