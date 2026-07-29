using PasswordManager.Core.Security;

namespace PasswordManager.Core.Tests.Security;

public class PasswordGeneratorTests
{
    [Fact]
    public void Generates_requested_length()
    {
        var pw = PasswordGenerator.Generate(new PasswordOptions(Length: 24));

        Assert.Equal(24, pw.Length);
    }

    [Fact]
    public void Uses_only_selected_character_classes()
    {
        var digitsOnly = new PasswordOptions(
            Length: 20, IncludeUppercase: false, IncludeLowercase: false,
            IncludeDigits: true, IncludeSymbols: false, ExcludeAmbiguous: false);

        var pw = PasswordGenerator.Generate(digitsOnly);

        Assert.All(pw, c => Assert.True(char.IsDigit(c)));
    }

    [Fact]
    public void Includes_at_least_one_from_each_selected_class()
    {
        var options = new PasswordOptions(
            Length: 8, IncludeUppercase: true, IncludeLowercase: true,
            IncludeDigits: true, IncludeSymbols: true, ExcludeAmbiguous: true);

        var pw = PasswordGenerator.Generate(options);

        Assert.Contains(pw, char.IsUpper);
        Assert.Contains(pw, char.IsLower);
        Assert.Contains(pw, char.IsDigit);
        Assert.Contains(pw, c => !char.IsLetterOrDigit(c));
    }

    [Fact]
    public void Excludes_ambiguous_characters_when_requested()
    {
        var options = new PasswordOptions(Length: 64, ExcludeAmbiguous: true);

        var pw = PasswordGenerator.Generate(options);

        Assert.DoesNotContain(pw, c => "O0oIl1".Contains(c));
    }

    [Fact]
    public void Throws_when_no_character_class_selected()
    {
        var none = new PasswordOptions(
            IncludeUppercase: false, IncludeLowercase: false,
            IncludeDigits: false, IncludeSymbols: false);

        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(none));
    }

    [Fact]
    public void Throws_when_length_too_short_to_cover_selected_classes()
    {
        // 4개 종류 선택인데 길이 3이면 각 종류 1자 보장 불가.
        var options = new PasswordOptions(Length: 3);

        Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(options));
    }

    [Fact]
    public void Successive_generations_differ()
    {
        var options = new PasswordOptions(Length: 32);

        Assert.NotEqual(PasswordGenerator.Generate(options), PasswordGenerator.Generate(options));
    }
}
