using System.Net;
using System.Net.Http.Json;
using Application.DTOs.Attendance;
using Domain.Common;
using Domain.Entities;
using Seguimiento.Tests.Infrastructure;

namespace Seguimiento.Tests.Integration;

public sealed class AttendanceEndpointIntegrationTests
{
    private readonly Guid _pfId = Guid.NewGuid();
    private readonly Guid _categoryId = Guid.NewGuid();

    private static string Endpoint(Guid categoryId) =>
        $"api/v1/categories/{categoryId}/attendance-today";

    [Fact]
    public async Task Given_StructureDown_When_GetAttendanceToday_Then_Returns200WithDegradedSummary()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var athleteId = Guid.NewGuid();
        await SeedDailyLoadAsync(factory, athleteId);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<CategoryAttendanceSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.PresentCount);
    }

    [Fact]
    public async Task Given_StructureDownAndNoLoads_When_GetAttendanceToday_Then_Returns200WithZeros()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<CategoryAttendanceSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.TotalRoster);
        Assert.Equal(0, summary.PresentCount);
    }

    [Fact]
    public async Task Given_StructureAlive_When_GetAttendanceToday_Then_CountsPresent()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var presentId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        await SeedDailyLoadAsync(factory, presentId);

        factory.Structure.RespondWithAthletes($$"""
            [
              {"id":"{{presentId}}","fullName":"Ana Roja"},
              {"id":"{{missingId}}","fullName":"Bruno Díaz"}
            ]
            """);

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<CategoryAttendanceSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(2, summary!.TotalRoster);
        Assert.Equal(1, summary.PresentCount);
        Assert.Equal(50d, summary.AttendancePercentage);
    }

    [Fact]
    public async Task Given_NoToken_When_GetAttendanceToday_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateClient().GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Given_AthleteRoleToken_When_GetAttendanceToday_Then_Returns403()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);

        var response = await factory.CreateAuthenticatedClient(_pfId, role: "Atleta")
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Given_UnknownCategoryInStructure_When_GetAttendanceToday_Then_Returns404()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        factory.Structure.RespondWith(HttpStatusCode.NotFound, """{"title":"Categoría no encontrada."}""");

        var response = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static Task SeedDailyLoadAsync(SeguimientoTestApplicationFactory factory, Guid athleteId) =>
        factory.ExecuteInDbContextAsync(db =>
        {
            db.DailyLoads.Add(new DailyLoad
            {
                AthleteId = athleteId,
                Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
                GymMinutes = 60,
                GymRpe = 7,
            });
            return db.SaveChangesAsync();
        });

    [Fact]
    public async Task Given_NoLoad_When_PfCorrectsPresent_Then_AttendanceCountsIt()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var athleteId = Guid.NewGuid();

        var patchResponse = await factory.CreateAuthenticatedClient(_pfId)
            .PatchAsJsonAsync($"api/v1/athletes/{athleteId}/attendance", new { present = true });
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        var getResponse = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // En modo degradado (Estructura caída) el padrón se desconoce; la corrección
        // manual se refleja en el conteo de presentes.
        var summary = await getResponse.Content.ReadFromJsonAsync<CategoryAttendanceSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(1, summary!.PresentCount);
    }

    [Fact]
    public async Task Given_Load_When_PfCorrectsAbsent_Then_AttendanceExcludesIt()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var athleteId = Guid.NewGuid();
        await SeedDailyLoadAsync(factory, athleteId);

        var patchResponse = await factory.CreateAuthenticatedClient(_pfId)
            .PatchAsJsonAsync($"api/v1/athletes/{athleteId}/attendance", new { present = false });
        Assert.Equal(HttpStatusCode.NoContent, patchResponse.StatusCode);

        var getResponse = await factory.CreateAuthenticatedClient(_pfId)
            .GetAsync(Endpoint(_categoryId));
        var summary = await getResponse.Content.ReadFromJsonAsync<CategoryAttendanceSummaryDto>();
        Assert.NotNull(summary);
        Assert.Equal(0, summary!.PresentCount);
    }

    [Fact]
    public async Task Given_NoToken_When_PatchAttendance_Then_Returns401()
    {
        await using var factory = await SeguimientoTestApplicationFactory.CreateAsync(_pfId);
        var athleteId = Guid.NewGuid();

        var response = await factory.CreateClient()
            .PatchAsJsonAsync($"api/v1/athletes/{athleteId}/attendance", new { present = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}