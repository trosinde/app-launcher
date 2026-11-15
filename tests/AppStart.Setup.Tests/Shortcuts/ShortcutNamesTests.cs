using AppStart.Setup.Engine.Shortcuts;

namespace AppStart.Setup.Tests.Shortcuts;

public sealed class ShortcutNamesTests
{
    [Theory]
    [InlineData("Messsoftware", "Messsoftware")]
    [InlineData("Waage Pro 3.2", "Waage Pro 3.2")]
    [InlineData("Daten: Export/Import", "Daten_ Export_Import")]
    [InlineData("a<b>c\"d|e?f*g\\h", "a_b_c_d_e_f_g_h")]
    [InlineData("Tab\there", "Tab_here")]
    [InlineData("  Name ...  ", "Name")]
    [InlineData("Version 1.", "Version 1")]
    [InlineData("CON", "_CON")]
    [InlineData("nul", "_nul")]
    [InlineData("com1", "_com1")]
    [InlineData("LPT9.app", "_LPT9.app")]
    [InlineData("CON .x", "_CON .x")]
    [InlineData("Console", "Console")]
    [InlineData("COM10", "COM10")]
    public void Sanitize(string name, string expected) => Assert.Equal(expected, ShortcutNames.Sanitize(name));

    [Fact]
    public void File_name_gets_lnk_extension() => Assert.Equal("Waage.lnk", ShortcutNames.ToFileName("Waage"));

    [Fact]
    public void Long_names_are_shortened_and_trimmed()
    {
        var name = new string('x', ShortcutNames.MaxLength - 1) + ". tail";
        Assert.Equal(new string('x', ShortcutNames.MaxLength - 1), ShortcutNames.Sanitize(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("???")]
    public void Unusable_names_are_rejected(string name)
    {
        var ex = Assert.Throws<ArgumentException>(() => ShortcutNames.Sanitize(name));
        Assert.Contains("kein gültiger Dateiname", ex.Message, StringComparison.Ordinal);
    }
}
