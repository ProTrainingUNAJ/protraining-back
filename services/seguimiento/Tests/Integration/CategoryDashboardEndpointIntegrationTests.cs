using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Domain.Entities;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

// KAN-504: el endpoint se prueba por el pipeline real (DI + controller + JWT), con
// Estructura falsa. Si falta el registro del handler en Program.cs, acá da 500.
public sealed class CategoryDashboardEndpointIntegrationTests
{
    [Fact]
    public async Task GetSummary_WithPfToken_ResolvesHandlerAndReturnsAggregatedSummary()
    {
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var athleteId = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(pfId);
        factory.Structure.Behavior = (request, _) =>
        {
            var json = request.RequestUri!.AbsolutePath.EndsWith("/athletes", StringComparison.Ordinal)
                ? $$"""[{"id":"{{athleteId}}","fullName":"Atleta Uno"}]"""
                : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        };

        await factory.ExecuteInDbContextAsync(async context =>
        {
            context.DailyLoads.Add(DailyLoad.Register(
                athleteId, new DateTime(2026, 11, 3), 60, 5, 0, null, 0, null, 0, null, 0, null));
            await context.SaveChangesAsync();
        });

        var response = await factory.CreateAuthenticatedClient(pfId).GetAsync(
            $"/api/v1/categories/{categoryId}/summary?from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("athleteCount").GetInt32());
        Assert.Equal(300, body.GetProperty("load").GetProperty("totalLoad").GetInt32());
    }

    [Fact]
    public async Task GetSummary_WithAthleteToken_ReturnsForbidden()
    {
        var pfId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(pfId);

        var response = await factory.CreateAuthenticatedClient(pfId, role: "Atleta").GetAsync(
            $"/api/v1/categories/{categoryId}/summary?from=2026-11-01&to=2026-11-30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
