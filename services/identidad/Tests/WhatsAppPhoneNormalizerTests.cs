using Domain.Services;
using Xunit;

namespace Identidad.Tests;

public class WhatsAppPhoneNormalizerTests
{
    [Theory]
    [InlineData("+54 9 11 2345-6789", "5491123456789")]
    [InlineData("+54-9-11-2345-6789", "5491123456789")]
    [InlineData("+54 (911) 2345 6789", "5491123456789")]
    [InlineData("  5491123456789  ", "5491123456789")]
    [InlineData("+34 612 34 56 78", "34612345678")]
    public void ValidateAndNormalize_ValidNumber_ReturnsOnlyDigitsWithCountryCode(string input, string expected)
    {
        var (isValid, normalized, error) = WhatsAppPhoneNormalizer.ValidateAndNormalize(input);

        Assert.True(isValid);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("54911abc6789", "no puede contener letras")]
    [InlineData("+54 9 11 234X-6789", "no puede contener letras")]
    [InlineData("12345", "entre 8 y 15 dígitos")] // Demasiado corto
    [InlineData("1234567890123456", "entre 8 y 15 dígitos")] // Demasiado largo (> 15 dígitos E.164)
    [InlineData("", "obligatorio")] // Vacio
    [InlineData("   ", "obligatorio")] // Espacios en blanco
    [InlineData(null, "obligatorio")] // Nulo
    public void ValidateAndNormalize_InvalidNumber_ReturnsFalseWithSpanishErrorMessage(string? input, string expectedErrorSubstring)
    {
        var (isValid, normalized, error) = WhatsAppPhoneNormalizer.ValidateAndNormalize(input);

        Assert.False(isValid);
        Assert.Null(normalized);
        Assert.NotNull(error);
        Assert.Contains(expectedErrorSubstring, error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildWhatsAppLink_ValidNumberWithoutInitialText_ReturnsWaMeLinkWithoutExtraCharacters()
    {
        var phone = "+54 9 11 2345-6789";

        var link = WhatsAppPhoneNormalizer.BuildWhatsAppLink(phone);

        Assert.Equal("https://wa.me/5491123456789", link);
    }

    [Fact]
    public void BuildWhatsAppLink_ValidNumberWithInitialText_ReturnsWaMeLinkWithEncodedText()
    {
        var phone = "+54 9 11 2345-6789";
        var message = "Hola profe, ¿cómo estás? Tengo una consulta sobre la rutina.";

        var link = WhatsAppPhoneNormalizer.BuildWhatsAppLink(phone, message);

        Assert.NotNull(link);
        Assert.StartsWith("https://wa.me/5491123456789?text=", link);
        // Verifica que no tenga caracteres crudos sin codificar (espacios, tildes, signos de pregunta)
        Assert.DoesNotContain(" ", link);
        Assert.DoesNotContain("¿", link);
        Assert.Contains("Hola%20profe", link);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildWhatsAppLink_WhenCoachHasNoPhone_ReturnsNullSoButtonDoesNotAppear(string? rawPhone)
    {
        var link = WhatsAppPhoneNormalizer.BuildWhatsAppLink(rawPhone);

        Assert.Null(link);
    }
}
