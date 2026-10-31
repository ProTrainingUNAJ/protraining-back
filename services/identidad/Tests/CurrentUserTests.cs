using System.Security.Claims;
using API.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class CurrentUserTests
{
    // Arma un CurrentUser con los claims indicados, como si vinieran de un token ya validado.
    private static CurrentUser CreateWithClaims(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))
        };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(context);
        return new CurrentUser(accessor.Object);
    }

    [Fact]
    public void Given_AthleteToken_When_ReadingIds_Then_UserIdIsTheSubAndPfIdIsThePfsId()
    {
        // En el token de un atleta el "sub" es su cuenta y el "pf_id" es el id de su preparador.
        var athleteId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var currentUser = CreateWithClaims(
            new Claim("sub", athleteId.ToString()),
            new Claim("pf_id", pfId.ToString()));

        Assert.Equal(athleteId, currentUser.UserId);
        Assert.Equal(pfId, currentUser.PfId);
        Assert.NotEqual(currentUser.UserId, currentUser.PfId);
    }

    [Fact]
    public void Given_SubMappedToNameIdentifier_When_ReadingUserId_Then_FallsBackToNameIdentifier()
    {
        // ASP.NET puede mapear "sub" a NameIdentifier según la configuración del JWT.
        var userId = Guid.NewGuid();
        var currentUser = CreateWithClaims(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        Assert.Equal(userId, currentUser.UserId);
    }

    [Fact]
    public void Given_TokenWithoutSub_When_ReadingUserId_Then_ReturnsNullEvenIfPfIdExists()
    {
        // Sin "sub" nunca se cae al pf_id: sería el bug original.
        var currentUser = CreateWithClaims(new Claim("pf_id", Guid.NewGuid().ToString()));

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void Given_SubThatIsNotAGuid_When_ReadingUserId_Then_ReturnsNull()
    {
        var currentUser = CreateWithClaims(new Claim("sub", "no-es-un-guid"));

        Assert.Null(currentUser.UserId);
    }
}