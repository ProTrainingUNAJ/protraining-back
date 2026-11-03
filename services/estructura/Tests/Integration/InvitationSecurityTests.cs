using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Categories;
using Application.DTOs.Organizations;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Estructura.Tests.Integration;

[Trait("Category", "Security")]
public sealed class InvitationSecurityTests : IAsyncDisposable
{
    private readonly EstructuraTestApplicationFactory _factory;
    private readonly HttpClient _clientPf;
    private readonly HttpClient _unauthenticatedClient;
    private readonly Guid _pfId;

    public InvitationSecurityTests()
    {
        _factory = new EstructuraTestApplicationFactory();
        _pfId = Guid.NewGuid();
        _clientPf = _factory.CreateAuthenticatedClient(_pfId);
        _unauthenticatedClient = _factory.CreateClient();
    }

    [Fact]
    public async Task Given_ValidToken_When_GetInvitation_Then_Returns200WithCategoryData_AndNeverExposesInternalAthleteIdsOrRawPfId()
    {
        // Arrange: PF autenticado crea organización y categoría, luego genera el token
        var orgResponse = await _clientPf.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Seguridad Test", "Club de prueba"));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var catResponse = await _clientPf.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org.Id, "Sub-20 Competitiva", "Categoría juvenil"));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        var tokenResponse = await _clientPf.PostAsync($"/api/v1/categories/{cat.Id}/invitation-token", null);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var invitation = await tokenResponse.Content.ReadFromJsonAsync<CategoryInvitationDto>();
        Assert.NotNull(invitation);

        // Act: Cliente anónimo (el atleta escaneando el QR) consulta la invitación pública
        var publicResponse = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{invitation.Token}");

        // Assert: 200 OK con datos públicos
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var rawContent = await publicResponse.Content.ReadAsStringAsync();
        var returnedInvitation = await publicResponse.Content.ReadFromJsonAsync<CategoryInvitationDto>();

        Assert.NotNull(returnedInvitation);
        Assert.Equal(invitation.Token, returnedInvitation.Token);
        Assert.Equal("Sub-20 Competitiva", returnedInvitation.CategoryName);
        Assert.Equal(cat.Id, returnedInvitation.CategoryId);

        // Seguridad: NO debe exponer el pf_id en crudo ni listas de atletas ni datos médicos
        Assert.DoesNotContain(_pfId.ToString(), rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("athletes", rawContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medicalRecord", rawContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Given_InvalidOrTamperedToken_When_GetInvitation_Then_Returns404NotFound_Never200Nor500()
    {
        // Arrange: Un token inventado y uno con un carácter alterado
        var inventedToken = "token_completamente_falso_1234567890";
        var tamperedToken = "94f35b3d076bc9c4fe850afb3264043X";

        // Act & Assert para token inventado: 404 Not Found, nunca 200 ni 500
        var inventedResponse = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{inventedToken}");
        Assert.Equal(HttpStatusCode.NotFound, inventedResponse.StatusCode);

        // Act & Assert para token adulterado: 404 Not Found, nunca 200 ni 500
        var tamperedResponse = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{tamperedToken}");
        Assert.Equal(HttpStatusCode.NotFound, tamperedResponse.StatusCode);

        // Act & Assert para token con espacios: 404 o 400
        var emptyResponse = await _unauthenticatedClient.GetAsync("/api/v1/invitations/%20");
        Assert.True(emptyResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Given_ExpiredTokenOver7Days_When_GetInvitation_Then_Returns410Gone_AndDoesNotReturnCategoryData()
    {
        // Arrange: Creamos un token y lo forzamos a estar vencido en la base de datos SQLite
        var orgResponse = await _clientPf.PostAsJsonAsync("/api/v1/organizations",
            new CreateOrganizationRequest("Club Vencimiento Test", "Club de prueba"));
        var org = await orgResponse.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.NotNull(org);

        var catResponse = await _clientPf.PostAsJsonAsync("/api/v1/categories",
            new CreateCategoryRequest(org.Id, "Sub-18 Vencida", "Categoría"));
        var cat = await catResponse.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(cat);

        var tokenResponse = await _clientPf.PostAsync($"/api/v1/categories/{cat.Id}/invitation-token", null);
        var invitation = await tokenResponse.Content.ReadFromJsonAsync<CategoryInvitationDto>();
        Assert.NotNull(invitation);

        // Modificamos directamente en la base SQLite para que la fecha de expiración sea hace 8 días
        using (var db = _factory.CreateDbContext(_pfId))
        {
            var dbInvitation = await db.CategoryInvitations.FirstAsync(i => i.Token == invitation.Token);
            typeof(CategoryInvitation).GetProperty(nameof(CategoryInvitation.ExpiresAt))!
                .SetValue(dbInvitation, DateTime.UtcNow.AddDays(-8));
            await db.SaveChangesAsync();
        }

        // Act: El atleta intenta acceder con el token vencido
        var expiredResponse = await _unauthenticatedClient.GetAsync($"/api/v1/invitations/{invitation.Token}");

        // Assert: 410 Gone y NO devuelve datos de la categoría
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);
        var content = await expiredResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sub-18 Vencida", content, StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }
}