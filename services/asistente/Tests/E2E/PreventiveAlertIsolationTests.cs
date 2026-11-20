using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Alerts;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Asistente.Tests.E2E;

/// <summary>
/// Pruebas end-to-end de creación, aislamiento multi-tenant y manejo de errores en Centro de Avisos (KAN-570).
/// Ejecuta la API completa de Asistente sobre PostgreSQL real (Testcontainers).
/// </summary>
[Trait("Category", "RequiresDocker")]
public sealed class PreventiveAlertIsolationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Given_TwoPfs_When_CreatingAndQueryingAlerts_Then_EnforcesTenantIsolationAndHttpSemantics()
    {
        await using var factory = await AsistenteE2EFactory.CreateAsync();

        var pfA = Guid.NewGuid();
        var pfB = Guid.NewGuid();
        var athleteOfPfA = Guid.NewGuid();
        var athleteOfPfB = Guid.NewGuid();
        var nonExistentAthlete = Guid.NewGuid();

        factory.External.AthleteOwners[athleteOfPfA] = pfA;
        factory.External.AthleteOwners[athleteOfPfB] = pfB;

        using var anonymousClient = factory.CreateClient();
        using var athleteRoleClient = factory.CreateAthleteClient(athleteOfPfA);
        using var clientPfA = factory.CreatePfClient(pfA);
        using var clientPfB = factory.CreatePfClient(pfB);

        var validRequestForA = new CreatePreventiveAlertRequestDto(
            AthleteId: athleteOfPfA,
            AthleteName: "Lucas Fernández",
            Reason: "Sobrecarga acumulada en los últimos 3 microciclos.",
            Suggestion: "Reducir volumen un 25% en la sesión de hoy.",
            Severity: "High",
            Type: "AccumulatedFatigue",
            SuggestedAdjustmentType: "LoadReduction",
            SuggestedAdjustmentPercentage: 25);

        // 1. Usuario no autenticado -> 401
        var unauthResponse = await anonymousClient.PostAsJsonAsync("/api/v1/alerts", validRequestForA);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthResponse.StatusCode);

        // 2. Usuario sin rol PreparadorFisico -> 403
        var forbiddenResponse = await athleteRoleClient.PostAsJsonAsync("/api/v1/alerts", validRequestForA);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        // 3. Request inválido (enum inválido) -> 400 ProblemDetails
        var invalidEnumRequest = validRequestForA with { Severity = "Extreme" };
        var badRequestResponse = await clientPfA.PostAsJsonAsync("/api/v1/alerts", invalidEnumRequest);
        Assert.Equal(HttpStatusCode.BadRequest, badRequestResponse.StatusCode);
        var badProblem = await badRequestResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(badProblem);
        Assert.Equal(400, badProblem.Status);

        // 4. PF A intenta crear aviso para atleta de PF B -> 404 ProblemDetails, sin persistir
        var crossTenantRequest = validRequestForA with
        {
            AthleteId = athleteOfPfB,
            AthleteName = "Atleta Ajeno"
        };
        var crossTenantResponse = await clientPfA.PostAsJsonAsync("/api/v1/alerts", crossTenantRequest);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
        var crossProblem = await crossTenantResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(crossProblem);
        Assert.Equal(404, crossProblem.Status);
        Assert.DoesNotContain(pfB.ToString(), await crossTenantResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // 5. PF A intenta crear aviso para athleteId inexistente -> 404 ProblemDetails
        var missingAthleteRequest = validRequestForA with { AthleteId = nonExistentAthlete };
        var missingResponse = await clientPfA.PostAsJsonAsync("/api/v1/alerts", missingAthleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

        // Confirmar que ningún aviso fue persistido aún para PF A ni PF B
        var listAfterRejections = await clientPfA.GetFromJsonAsync<List<PreventiveAlertResponseDto>>("/api/v1/alerts?onlyUnreviewed=false");
        Assert.NotNull(listAfterRejections);
        Assert.Empty(listAfterRejections);

        // 6. Estructura no disponible -> 503 ProblemDetails controlado, sin persistir
        factory.External.StructureAthleteStatusOverride = HttpStatusCode.ServiceUnavailable;
        var unavailableResponse = await clientPfA.PostAsJsonAsync("/api/v1/alerts", validRequestForA);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailableResponse.StatusCode);
        var unavailableProblem = await unavailableResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        Assert.NotNull(unavailableProblem);
        Assert.Equal(503, unavailableProblem.Status);
        factory.External.StructureAthleteStatusOverride = null;

        // 7. PF A crea aviso para su propio atleta -> 201 Created y persistido
        var createdResponse = await clientPfA.PostAsJsonAsync("/api/v1/alerts", validRequestForA);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdDto = await createdResponse.Content.ReadFromJsonAsync<PreventiveAlertResponseDto>(JsonOptions);
        Assert.NotNull(createdDto);
        Assert.Equal(athleteOfPfA, createdDto.AthleteId);
        Assert.Equal("High", createdDto.Severity);
        Assert.Equal("LoadReduction", createdDto.SuggestedAdjustmentType);
        Assert.Equal(25, createdDto.SuggestedAdjustmentPercentage);
        Assert.Equal("Pending", createdDto.Status);

        // 8. PF A puede consultar su propio aviso por ID -> 200 OK
        var getOwnResponse = await clientPfA.GetAsync($"/api/v1/alerts/{createdDto.Id}");
        Assert.Equal(HttpStatusCode.OK, getOwnResponse.StatusCode);

        // 9. PF B NO puede consultar el aviso de PF A por ID -> 404 NotFound
        var getOtherResponse = await clientPfB.GetAsync($"/api/v1/alerts/{createdDto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getOtherResponse.StatusCode);

        // 10. PF B tampoco lo ve en GET /api/v1/alerts
        var listPfB = await clientPfB.GetFromJsonAsync<List<PreventiveAlertResponseDto>>("/api/v1/alerts?onlyUnreviewed=false");
        Assert.NotNull(listPfB);
        Assert.Empty(listPfB);
    }
}
