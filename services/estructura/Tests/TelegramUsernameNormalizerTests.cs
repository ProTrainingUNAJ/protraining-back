using Domain.Services;
using Xunit;

namespace Estructura.Tests;

public class TelegramUsernameNormalizerTests
{
    [Theory]
    [InlineData("@lucas_fernandez", "lucas_fernandez")]
    [InlineData("lucas_fernandez", "lucas_fernandez")]
    [InlineData("  @Lucas_99  ", "Lucas_99")]
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
    [InlineData("abcd")]
    [InlineData("@@lucas")]
    [InlineData("lucas-fernandez")]
    [InlineData("lucas fernandez")]
    public void ValidateAndNormalize_InvalidUsername_ReturnsFalseWithError(string input)
    {
        var (isValid, normalized, error) = TelegramUsernameNormalizer.ValidateAndNormalize(input);

        Assert.False(isValid);
        Assert.Null(normalized);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
