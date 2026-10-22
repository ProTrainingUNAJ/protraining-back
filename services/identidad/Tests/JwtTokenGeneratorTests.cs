using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using Domain.Entities;
using Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Identidad.Tests;

public class JwtTokenGeneratorTests
{
    private readonly JwtTokenGenerator _generator;
    private const string SecretKey = "ClaveSecretaSuperLargaYProtegidaParaTesting1234567890!";
    private const string Issuer = "ProTrainingIdentidad";
    private const string Audience = "ProTrainingApp";

    public JwtTokenGeneratorTests()
    {
        var mockConfiguration = new Mock<IConfiguration>();
        mockConfiguration.Setup(c => c["Jwt:Key"]).Returns(SecretKey);
        mockConfiguration.Setup(c => c["Jwt:Issuer"]).Returns(Issuer);
        mockConfiguration.Setup(c => c["Jwt:Audience"]).Returns(Audience);

        _generator = new JwtTokenGenerator(mockConfiguration.Object);
    }

    [Fact]
    public void Given_ValidUser_When_GenerateToken_Then_DecodedJwt_Contains_Required_Sub_Role_And_PfId_Claims()
    {
        // arrange
        var userId = Guid.NewGuid();
        var pfId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "atleta@protraining.com",
            FullName = "Atleta de Prueba",
            PfId = pfId
        };

        // act
        // genera el token JWT real firmado
        var tokenString = _generator.GenerateToken(user, "Atleta");

        // decodifica el JWT resultante para inspeccionar los claims (criterio de aceptacion 2)
        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        // assert
        Assert.NotNull(jwtToken);

        // verifica claim sub con el id del usuario
        var subClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "sub" || c.Type == JwtRegisteredClaimNames.Sub);
        Assert.NotNull(subClaim);
        Assert.Equal(userId.ToString(), subClaim.Value);

        // verifica claim role
        var roleClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "role");
        Assert.NotNull(roleClaim);
        Assert.Equal("Atleta", roleClaim.Value);

        // verifica claim pf_id
        var pfIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "pf_id");
        Assert.NotNull(pfIdClaim);
        Assert.Equal(pfId.ToString(), pfIdClaim.Value);

        // verifica claim email
        var emailClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == JwtRegisteredClaimNames.Email);
        Assert.NotNull(emailClaim);
        Assert.Equal(user.Email, emailClaim.Value);
    }

    [Fact]
    public void Given_UserWithoutPfId_When_GenerateToken_Then_DecodedJwt_PfId_DefaultsTo_UserId()
    {
        // arrange
        var userId = Guid.NewGuid();
        var user = new User
        {
            Id = userId,
            Email = "profesor@protraining.com",
            FullName = "Profesor de Prueba",
            PfId = null // si no tiene pf_id asignado, usa su propio id
        };

        // act
        var tokenString = _generator.GenerateToken(user, "Profesor");

        var handler = new JwtSecurityTokenHandler();
        var jwtToken = handler.ReadJwtToken(tokenString);

        // assert
        var pfIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "pf_id");
        Assert.NotNull(pfIdClaim);
        Assert.Equal(userId.ToString(), pfIdClaim.Value);
    }
}
