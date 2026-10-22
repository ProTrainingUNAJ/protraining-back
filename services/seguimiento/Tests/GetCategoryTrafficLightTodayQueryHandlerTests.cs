using Application.DTOs.Structure;
using Application.DTOs.TrafficLight;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.TrafficLight.GetCategoryTrafficLightToday;
using Domain.Common;
using Domain.Entities;
using Domain.Services;
using Moq;

namespace Seguimiento.Tests;

// Tests del semáforo por categoría (KAN-364): cruza los atletas de Estructura con los
// check-ins locales del día y degrada a esos check-ins si Estructura no responde.
public class GetCategoryTrafficLightTodayQueryHandlerTests
{
    private readonly Guid _categoryId = Guid.NewGuid();
    private readonly Mock<IStructureClient> _structure = new();
    private readonly Mock<ICheckInRepository> _checkIns = new();
    private readonly TrafficLightEngine _engine = new();

    private GetCategoryTrafficLightTodayQueryHandler CreateHandler() =>
        new(_structure.Object, _checkIns.Object, _engine);

    private static CategoryAthleteDto CreateAthleteDto(Guid id, string fullName) =>
        new() { Id = id, FullName = fullName };

    private static CheckIn CreateCheckIn(Guid athleteId, TrafficLight color, string? reason = null) =>
        new()
        {
            AthleteId = athleteId,
            Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
            TrafficLight = color,
            TrafficLightReason = reason
        };

    private void SetupAthletes(params CategoryAthleteDto[] athletes) =>
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes.ToList());

    private void SetupCheckIns(params CheckIn[] checkIns) =>
        _checkIns
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns.ToList());

    [Fact]
    public async Task HandleAsync_MixesReportedAndMissing_ThenOrdersBySeverity()
    {
        var redTrafficLight = Guid.NewGuid();
        var missingCheckIn = Guid.NewGuid();
        var greenTrafficLight = Guid.NewGuid();

        // Estructura trae los atletas en orden alfabético; el result debe reordenar por severidad.
        SetupAthletes(CreateAthleteDto(redTrafficLight, "Ana Rojas"), CreateAthleteDto(missingCheckIn, "Bruno Díaz"), CreateAthleteDto(greenTrafficLight, "Carla Sosa"));
        SetupCheckIns(
            CreateCheckIn(redTrafficLight, TrafficLight.Red, "Lesión activa"),
            CreateCheckIn(greenTrafficLight, TrafficLight.Green, "Estado óptimo"));

        var result = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.Equal(3, result.Count);

        // Rojo primero, con el color y el motivo persistidos en el check-in.
        Assert.Equal(redTrafficLight, result[0].AthleteId);
        Assert.Equal("Ana Rojas", result[0].AthleteName);
        Assert.Equal("Red", result[0].TrafficLight);
        Assert.Equal("Lesión activa", result[0].TrafficLightReason);
        Assert.True(result[0].HasReportedToday);
        Assert.Equal(20, result[0].ResultValue);
        Assert.NotNull(result[0].Indicators);

        // Sin check-in: Amarillo con el motivo del motor, sin fecha de reporte ni indicadores.
        Assert.Equal(missingCheckIn, result[1].AthleteId);
        Assert.Equal("Yellow", result[1].TrafficLight);
        Assert.Contains("sin check-in", result[1].TrafficLightReason!, StringComparison.Ordinal);
        Assert.False(result[1].HasReportedToday);
        Assert.Null(result[1].ReportedAt);
        Assert.Equal(50, result[1].ResultValue);
        Assert.Null(result[1].Indicators);

        // Verde al final.
        Assert.Equal(greenTrafficLight, result[2].AthleteId);
        Assert.Equal("Green", result[2].TrafficLight);
        Assert.True(result[2].HasReportedToday);
        Assert.Equal(90, result[2].ResultValue);
        Assert.NotNull(result[2].Indicators);
    }

    [Fact]
    public async Task HandleAsync_WithReportedAthlete_KeepsPersistedColorAndMapsReportedAt()
    {
        var athleteId = Guid.NewGuid();
        var checkIn = CreateCheckIn(athleteId, TrafficLight.Yellow, "Señales de alerta: poco sueño");
        checkIn.SleepHours = 4.5;
        checkIn.SleepQuality = 2;
        checkIn.EffortRate = 8;
        checkIn.IsSick = true;
        checkIn.HasActiveInjury = false;
        checkIn.PainZones = "cuadriceps_derecho:3";
        SetupAthletes(CreateAthleteDto(athleteId, "Lucas Fernández"));
        SetupCheckIns(checkIn);

        var result = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        var summary = Assert.Single(result);
        Assert.Equal("Yellow", summary.TrafficLight);
        Assert.Equal("Señales de alerta: poco sueño", summary.TrafficLightReason);
        Assert.True(summary.HasReportedToday);
        Assert.Equal(checkIn.CreatedAt, summary.ReportedAt);
        Assert.Equal(50, summary.ResultValue);

        // Los indicadores reflejan los valores crudos del check-in (KAN-365).
        var indicators = Assert.IsType<TrafficLightIndicatorsDto>(summary.Indicators);
        Assert.Equal(4.5, indicators.SleepHours);
        Assert.Equal(2, indicators.SleepQuality);
        Assert.Equal(8, indicators.EffortRate);
        Assert.True(indicators.IsSick);
        Assert.False(indicators.HasActiveInjury);
        Assert.Equal(checkIn.PainZones, indicators.PainZones);
    }

    [Fact]
    public async Task HandleAsync_WithoutCheckIn_UsesEngineWithNullData_YellowMissingCheckIn()
    {
        var athleteId = Guid.NewGuid();
        SetupAthletes(CreateAthleteDto(athleteId, "Sofía Ruiz"));
        SetupCheckIns();

        var result = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        var summary = Assert.Single(result);
        Assert.Equal("Yellow", summary.TrafficLight);
        Assert.Contains("sin check-in", summary.TrafficLightReason!, StringComparison.Ordinal);
        Assert.False(summary.HasReportedToday);
    }

    [Fact]
    public async Task HandleAsync_WhenStructureUnavailable_DegradesToTodaysCheckIns()
    {
        // Resiliencia (KAN-364): Estructura caída no puede tumbar la pantalla del PF.
        var reportedAthleteId = Guid.NewGuid();
        var otherAthleteId = Guid.NewGuid();
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureUnavailableException());
        _checkIns
            .Setup(repo => repo.GetByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>
            {
                CreateCheckIn(reportedAthleteId, TrafficLight.Red, "Lesión activa"),
                CreateCheckIn(otherAthleteId, TrafficLight.Green, "Estado óptimo")
            });

        var result = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.Equal(2, result.Count);
        Assert.All(result, item =>
        {
            Assert.Equal(string.Empty, item.AthleteName);
            Assert.True(item.HasReportedToday);
            Assert.NotNull(item.Indicators);
        });
        Assert.Equal("Red", result[0].TrafficLight);
        Assert.Equal("Lesión activa", result[0].TrafficLightReason);
        Assert.Equal("Green", result[1].TrafficLight);

        // El cruce por atletas no se intenta si Estructura ya falló.
        _checkIns.Verify(
            repo => repo.GetByAthletesAndDateAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryNotFound_ThenPropagates()
    {
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CategoryNotFoundException());

        await Assert.ThrowsAsync<CategoryNotFoundException>(
            () => CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId)));
    }

    [Fact]
    public async Task HandleAsync_UsesArgentinaDate_WhenQueryingCheckIns()
    {
        DateTime? receivedDate = null;
        SetupAthletes(CreateAthleteDto(Guid.NewGuid(), "Ana Rojas"));
        _checkIns
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, DateTime, CancellationToken>((_, date, _) => receivedDate = date)
            .ReturnsAsync(new List<CheckIn>());

        await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.NotNull(receivedDate);
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(receivedDate!.Value));
    }
}
