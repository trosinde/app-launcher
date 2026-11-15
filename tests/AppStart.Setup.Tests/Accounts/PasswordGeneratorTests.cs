using AppStart.Setup.Engine.Accounts;

namespace AppStart.Setup.Tests.Accounts;

public class PasswordGeneratorTests
{
    [Fact]
    public void Password_meets_every_complexity_class()
    {
        Span<char> buffer = stackalloc char[PasswordGenerator.Length];
        var length = PasswordGenerator.Generate(buffer);
        var password = buffer[..length].ToString();

        Assert.Equal(48, length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, c => !char.IsLetterOrDigit(c));
        Assert.DoesNotContain('\0', password);
        Assert.All(password, c => Assert.True(c is > ' ' and < (char)127));
    }

    [Fact]
    public void Passwords_differ()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ =>
        {
            var buffer = new char[PasswordGenerator.Length];
            PasswordGenerator.Generate(buffer);
            return new string(buffer);
        }).ToHashSet();

        Assert.Equal(50, passwords.Count);
    }

    [Fact]
    public void Too_small_buffer_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(new char[PasswordGenerator.Length - 1]));
    }
}
