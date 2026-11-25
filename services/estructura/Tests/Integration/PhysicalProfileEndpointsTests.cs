using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Application.DTOs.Athletes;
using Application.UseCases.Athletes.UpdateMyPhysicalProfile;
using Domain.Entities;
using Estructura.Tests.Infrastructure;
using Xunit;

namespace Estructura.Tests.Integration;

public sealed class PhysicalProfileEndpointsTests : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly EstructuraTestApplicationFactory _factory = new();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Athlete_And_Pf_CanPersistAndRead_HeightWeightGoalBirthDateAndGender()
    {
        var pfId = Guid.NewGuid();
        var athleteAccountId = Guid.NewGuid();

        Guid athleteId;
        using (var db = _factory.CreateDbContext(pfId))
        {
            var athlete = Athlete.CreateAdult(
                firstName: "Lucas",
                lastName: "Gómez",
                email: $"lucas-{Guid.NewGuid():N}@test.com",
                phone: "+54 9 11 5555-1234",
                emergencyContact: null);
            athlete.LinkAccount(athleteAccountId);
            db.Athletes.Add(athlete);
            await db.SaveChangesAsync();
            athleteId = athlete.Id;
        }

        using var pfClient = _factory.CreateAuthenticatedClient(pfId, "PreparadorFisico");
        using var athleteClient = _factory.CreateAuthenticatedClient(pfId, "Atleta", athleteAccountId);

        // 1. El atleta guarda altura, peso, objetivo, fecha de nacimiento y género desde Configuración
        var athletePatch = new UpdatePhysicalProfileRequest(
            WeightKg: 76.5m,
            AthleticGoal: AthleticGoalLabels.Performance,
            BirthDate: new DateOnly(2000, 5, 15),
            Gender: "Masculino",
            HeightCm: 178.4m);

        var patchRes = await athleteClient.PatchAsJsonAsync("/api/v1/me/physical-profile", athletePatch);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var patchDto = await patchRes.Content.ReadFromJsonAsync<UpdateMyPhysicalProfileResponseDto>(JsonOptions);
        Assert.NotNull(patchDto);
        Assert.Equal(76.5m, patchDto.WeightKg);
        Assert.Equal(178.4m, patchDto.HeightCm);
        Assert.Equal(AthleticGoalLabels.Performance, patchDto.AthleticGoal);
        Assert.Equal(new DateOnly(2000, 5, 15), patchDto.BirthDate);
        Assert.Equal("Masculino", patchDto.Gender);

        // 2. GET /api/v1/me refleja inmediatamente todos los datos guardados
        var me = await athleteClient.GetFromJsonAsync<MeDto>("/api/v1/me", JsonOptions);
        Assert.NotNull(me);
        Assert.Equal(76.5m, me.WeightKg);
        Assert.Equal(178.4m, me.HeightCm);
        Assert.Equal(AthleticGoalLabels.Performance, me.AthleticGoal);
        Assert.Equal(new DateOnly(2000, 5, 15), me.BirthDate);
        Assert.Equal("Masculino", me.Gender);

        // 3. El PF actualiza altura/peso/objetivo sin mandar birthDate ni gender -> se preservan birthDate y gender
        var pfPartialPatch = new UpdatePhysicalProfileRequest(
            WeightKg: 78.0m,
            AthleticGoal: AthleticGoalLabels.GainMuscle,
            HeightCm: 181.0m);

        var pfPatchRes = await pfClient.PatchAsJsonAsync($"/api/v1/athletes/{athleteId}/physical-profile", pfPartialPatch);
        Assert.Equal(HttpStatusCode.OK, pfPatchRes.StatusCode);

        var updatedAthlete = await pfPatchRes.Content.ReadFromJsonAsync<AthleteDto>(JsonOptions);
        Assert.NotNull(updatedAthlete);
        Assert.Equal(78.0m, updatedAthlete.WeightKg);
        Assert.Equal(181.0m, updatedAthlete.HeightCm);
        Assert.Equal(AthleticGoalLabels.GainMuscle, updatedAthlete.AthleticGoal);
        Assert.Equal(new DateOnly(2000, 5, 15), updatedAthlete.BirthDate);
        Assert.Equal("Masculino", updatedAthlete.Gender);

        // 4. GET /api/v1/athletes/{id} y GET /api/v1/me devuelven la altura actualizada
        var fetchedAthlete = await pfClient.GetFromJsonAsync<AthleteDto>($"/api/v1/athletes/{athleteId}", JsonOptions);
        Assert.NotNull(fetchedAthlete);
        Assert.Equal(181.0m, fetchedAthlete.HeightCm);
        Assert.Equal(78.0m, fetchedAthlete.WeightKg);

        var meAfterPf = await athleteClient.GetFromJsonAsync<MeDto>("/api/v1/me", JsonOptions);
        Assert.NotNull(meAfterPf);
        Assert.Equal(181.0m, meAfterPf.HeightCm);
        Assert.Equal(78.0m, meAfterPf.WeightKg);

        // 5. Altura fuera de rango devuelve 400 Bad Request y no sobreescribe los datos
        var invalidRes = await athleteClient.PatchAsJsonAsync(
            "/api/v1/me/physical-profile",
            new UpdatePhysicalProfileRequest(WeightKg: 78.0m, AthleticGoal: AthleticGoalLabels.Maintain, HeightCm: 30m));
        Assert.Equal(HttpStatusCode.BadRequest, invalidRes.StatusCode);

        var meAfterInvalid = await athleteClient.GetFromJsonAsync<MeDto>("/api/v1/me", JsonOptions);
        Assert.Equal(181.0m, meAfterInvalid?.HeightCm);
    }
}
