using Domain.Services;
using Xunit;

namespace Identidad.Tests;

public class TelegramUsernameNormalizerTests
{
    [Theory]
    [InlineData("@profe_juan", "profe_juan")]
    [InlineData("profe_juan", "profe_juan")]
    [InlineData("  @Profe_123  ", "Profe_123")]
    [InlineData("abcde", "abcde")]
    [InlineData("abcdefghijklmnopqrstuvwxyz123456", "abcdefghijklmnopqrstuvwxyz123456")]
    public void ValidateAndNormalize_ValidUsername_ReturnsWithoutAt(string input, string expected)
    {
        var (isValid, normalized, error) = TelegramUsernameNormalizer.ValidateAndNormalize(input);

        Assert.True(isValid);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateAndNormalize_NullOrWhitespace_ReturnsValidNull(string? input)
    {
        var (isValid, normalized, error) = TelegramUsernameNormalizer.ValidateAndNormalize(input);

        Assert.True(isValid);
        Assert.Null(normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("abcd", "entre 5 y 32 caracteres")]
    [InlineData("@abcd", "entre 5 y 32 caracteres")]
    [InlineData("abcdefghijklmnopqrstuvwxyz1234567", "entre 5 y 32 caracteres")]
    [InlineData("@@profe_juan", "múltiples '@'")]
    [InlineData("profe@juan", "múltiples '@'")]
    [InlineData("profe-juan", "letras, números y guión bajo")]
    [InlineData("profe juan", "letras, números y guión bajo")]
    [InlineData("profe.juan", "letras, números y guión bajo")]
    public void ValidateAndNormalize_InvalidUsername_ReturnsFalseWithSpanishError(string input, string expectedSubstring)
    {
        var (isValid, normalized, error) = TelegramUsernameNormalizer.ValidateAndNormalize(input);

        Assert.False(isValid);
        Assert.Null(normalized);
        Assert.NotNull(error);
        Assert.Contains(expectedSubstring, error, StringComparison.OrdinalIgnoreCase);
    }
}
