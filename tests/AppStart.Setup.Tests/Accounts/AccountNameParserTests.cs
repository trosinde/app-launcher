using AppStart.Setup.Engine.Accounts;

namespace AppStart.Setup.Tests.Accounts;

public class AccountNameParserTests
{
    private const string Machine = "PC-042";

    [Theory]
    [InlineData("user", "PC-042\\user")]
    [InlineData(".\\user", "PC-042\\user")]
    [InlineData("PC-042\\user", "PC-042\\user")]
    [InlineData("pc-042\\user", "PC-042\\user")]
    [InlineData("  .\\appstart-user  ", "PC-042\\appstart-user")]
    [InlineData("AppStart Users", "PC-042\\AppStart Users")]
    public void Local_forms_are_qualified_with_the_computer_name(string input, string expected)
    {
        var name = AccountNameParser.Parse(input, Machine);

        Assert.Equal(AccountNameKind.Local, name.Kind);
        Assert.Equal(expected, name.Qualified);
        Assert.Null(name.Sid);
    }

    [Theory]
    [InlineData("CONTOSO\\jdoe", "CONTOSO", "jdoe")]
    [InlineData("contoso.local\\Max.Muster", "contoso.local", "Max.Muster")]
    [InlineData("AzureAD\\LiamMuster", "AzureAD", "LiamMuster")]
    public void Domain_form_keeps_domain_and_user(string input, string domain, string user)
    {
        var name = AccountNameParser.Parse(input, Machine);

        Assert.Equal(AccountNameKind.Domain, name.Kind);
        Assert.Equal(domain, name.Domain);
        Assert.Equal(user, name.User);
        Assert.Equal($"{domain}\\{user}", name.Qualified);
    }

    [Theory]
    [InlineData("S-1-5-21-1004336348-1177238915-682003330-1001")]
    [InlineData("s-1-5-32-544")]
    public void Sid_strings_are_recognised(string input)
    {
        var name = AccountNameParser.Parse(input, Machine);

        Assert.Equal(AccountNameKind.Sid, name.Kind);
        Assert.Equal(input.ToUpperInvariant(), name.Qualified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\\b\\c")]
    [InlineData("\\user")]
    [InlineData("DOMAIN\\")]
    [InlineData(".\\")]
    [InlineData("user@contoso.com")]
    [InlineData("us/er")]
    [InlineData("us*er")]
    [InlineData("us\"er")]
    [InlineData("DOM[AIN\\user")]
    [InlineData("DOMAIN\\ user")]
    [InlineData("...")]
    [InlineData("S-1-5-abc")]
    [InlineData("us\ter")]
    public void Invalid_input_is_rejected_with_a_German_message(string? input)
    {
        Assert.False(AccountNameParser.TryParse(input, Machine, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));

        var ex = Assert.Throws<ArgumentException>(() => AccountNameParser.Parse(input, Machine));
        Assert.StartsWith(error, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Upn_gets_a_hint_to_the_domain_form()
    {
        AccountNameParser.TryParse("jdoe@contoso.com", Machine, out _, out var error);

        Assert.Contains("DOMAIN\\User", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_machine_name_is_a_programming_error()
    {
        Assert.ThrowsAny<ArgumentException>(() => AccountNameParser.Parse("user", " "));
    }
}
