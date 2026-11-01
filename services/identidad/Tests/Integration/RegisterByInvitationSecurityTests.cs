using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Application.DTOs.Auth;
using Identidad.Tests.Infrastructure;
using Xunit;

namespace Identidad.Tests.Integration;

[Trait("Category", "Security")]
public sealed class RegisterByInvitationSecurityTests : IAsyncDisposable
{
    private readonly IdentidadTestApplicationFactory _factory;
    private readonly HttpClient _client;

    public RegisterByInvitationSecurityTests()
    {
        _factory = new IdentidadTestApplicationFactory();
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Given_RoleEscalationAttemptInPayload_When_Register_Then_IgnoresRoleInBody_AndUserIsStrictlyCreatedAsAtleta()
    {
        // Arrange: Token válido en Estructura
        var token = "token-valido-escalada";
        _factory.Structure.RespondWithInvitation(token, Guid.NewGuid(), "Sub-20", DateTime.UtcNow.AddDays(5));

        // Payload malicioso: Intenta inyectar role: PreparadorFisico en el JSON
        var rawMaliciousPayload = $$"""
        {
            "email": "atleta.hacker@protraining.com",
            "password": "PasswordSegura123!",
            "fullName": "Atleta Astuto",
            "token": "{{token}}",
            "role": "PreparadorFisico",
            "roles": ["PreparadorFisico", "Admin"]
        }
        """;

        var content = new StringContent(rawMaliciousPayload, Encoding.UTF8, "application/json");

        // Act: Enviar registro
        var response = await _client.PostAsync("/api/v1/auth/register-by-invitation", content);

        // Assert: 200 OK pero con rol Atleta estricto
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        Assert.NotNull(result);
        Assert.Equal("Atleta", result.User.Role);

        // Verificar el token JWT devuelto: el claim "role" debe ser "Atleta"
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.AccessToken);
        var roleClaim = jwt.Claims.FirstOrDefault(c => c.Type == "role" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role");
        Assert.NotNull(roleClaim);
        Assert.Equal("Atleta", roleClaim.Value);

        // Seguridad en BD: Comprobar directamente en la base SQLite que solo tiene rol Atleta
        var userInDb = await _factory.FindUserByEmailAsync("atleta.hacker@protraining.com");
        Assert.NotNull(userInDb);
        var dbRoles = await _factory.GetUserRolesAsync(userInDb);
        Assert.Single(dbRoles);
        Assert.Equal("Atleta", dbRoles[0]);
        Assert.DoesNotContain("PreparadorFisico", dbRoles);
    }

    [Fact]
    public async Task Given_InvalidOrTamperedToken_When_Register_Then_ReturnsError_AndNoUserIsCreatedInDatabase()
    {
        // Arrange: Estructura responde 404 (token inexistente)
        _factory.Structure.RespondWithNotFound();

        var request = new RegisterByInvitationRequestDto(
            Email: "fantasma@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Usuario Fantasma",
            Token: "token-inexistente-123"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-by-invitation", request);

        // Assert HTTP: 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Assert BD: Se comprueba en la base que no se creó ningún usuario
        var userInDb = await _factory.FindUserByEmailAsync("fantasma@protraining.com");
        Assert.Null(userInDb);
    }

    [Fact]
    public async Task Given_ExpiredToken_When_Register_Then_Returns410Gone_AndNoUserIsCreatedInDatabase()
    {
        // Arrange: Estructura responde 410 Gone (token vencido)
        _factory.Structure.RespondWithExpired("token-vencido");

        var request = new RegisterByInvitationRequestDto(
            Email: "vencido@protraining.com",
            Password: "PasswordSegura123!",
            FullName: "Usuario Vencido",
            Token: "token-vencido"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-by-invitation", request);

        // Assert HTTP: 410 Gone
        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        // Assert BD: Cero usuarios creados en la base de datos
        var userInDb = await _factory.FindUserByEmailAsync("vencido@protraining.com");
        Assert.Null(userInDb);
    }

    [Fact]
    public async Task Given_EmailAlreadyExistsWithPreparadorFisicoRole_When_Register_Then_Returns403Forbidden_AndDatabaseIsUnchanged()
    {
        // Arrange: Usuario ya registrado como PF
        var pfEmail = "pf.titular@protraining.com";
        await _factory.SeedUserAsync(pfEmail, "Prof. Titular", "PreparadorFisico");

        var token = "token-valido-pf";
        _factory.Structure.RespondWithInvitation(token, Guid.NewGuid(), "Sub-20", DateTime.UtcNow.AddDays(5));

        var request = new RegisterByInvitationRequestDto(
            Email: pfEmail,
            Password: "PasswordSegura123!",
            FullName: "Intento Registro PF",
            Token: token
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-by-invitation", request);

        // Assert HTTP: 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Assert BD: El usuario sigue siendo únicamente PF y no se le asignó Atleta
        var userInDb = await _factory.FindUserByEmailAsync(pfEmail);
        Assert.NotNull(userInDb);
        var roles = await _factory.GetUserRolesAsync(userInDb);
        Assert.Contains("PreparadorFisico", roles);
        Assert.DoesNotContain("Atleta", roles);
    }

    [Fact]
    public async Task Given_EmailAlreadyExistsWithAtletaRole_When_Register_Then_Returns409Conflict_WithExactMessage()
    {
        // Arrange: Usuario ya registrado como Atleta
        var atletaEmail = "atleta.existente@protraining.com";
        await _factory.SeedUserAsync(atletaEmail, "Atleta Repetido", "Atleta");

        var token = "token-valido-atleta";
        _factory.Structure.RespondWithInvitation(token, Guid.NewGuid(), "Sub-20", DateTime.UtcNow.AddDays(5));

        var request = new RegisterByInvitationRequestDto(
            Email: atletaEmail,
            Password: "PasswordSegura123!",
            FullName: "Atleta Repetido",
            Token: token
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register-by-invitation", request);

        // Assert HTTP: 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ya tenés cuenta, iniciá sesión.", raw);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}