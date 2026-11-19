using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identidad.Tests;

public class GoogleTokenValidatorTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task Given_MissingOrBlankClientId_When_ValidateAsync_Then_FailsClosed(string? clientId)
    {
        var options = Options.Create(new GoogleAuthOptions { ClientId = clientId! });
        var validator = new GoogleTokenValidator(options, NullLogger<GoogleTokenValidator>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync("header.payload.signature"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Given_EmptyIdToken_When_ValidateAsync_Then_ThrowsArgumentException(string idToken)
    {
        var options = Options.Create(new GoogleAuthOptions { ClientId = "123456-test.apps.googleusercontent.com" });
        var validator = new GoogleTokenValidator(options, NullLogger<GoogleTokenValidator>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() => validator.ValidateAsync(idToken));
    }

    [Fact]
    public async Task Given_InvalidJwtSignatureOrFormat_When_ValidateAsync_Then_RejectsToken()
    {
        var options = Options.Create(new GoogleAuthOptions { ClientId = "123456-test.apps.googleusercontent.com" });
        var validator = new GoogleTokenValidator(options, NullLogger<GoogleTokenValidator>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            validator.ValidateAsync("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjMifQ.invalidsignature"));
    }
}
