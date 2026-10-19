using Application.DTOs.Structure;
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

    private static CategoryAthleteDto Atleta(Guid id, string fullName) =>
        new() { Id = id, FullName = fullName };

    private static CheckIn CheckInDe(Guid athleteId, TrafficLight color, string? motivo = null) =>
        new()
        {
            AthleteId = athleteId,
            Date = ArgentinaTime.Today.ToDateTime(TimeOnly.MinValue),
            TrafficLight = color,
            TrafficLightReason = motivo
        };

    private void ConfigurarAtletas(params CategoryAthleteDto[] athletes) =>
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(_categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(athletes.ToList());

    private void ConfigurarCheckIns(params CheckIn[] checkIns) =>
        _checkIns
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(checkIns.ToList());

    [Fact]
    public async Task HandleAsync_MixesReportedAndMissing_ThenOrdersBySeverity()
    {
        var rojo = Guid.NewGuid();
        var sinCheckIn = Guid.NewGuid();
        var verde = Guid.NewGuid();

        // Estructura trae los atletas en orden alfabético; el resultado debe reordenar por severidad.
        ConfigurarAtletas(Atleta(rojo, "Ana Rojas"), Atleta(sinCheckIn, "Bruno Díaz"), Atleta(verde, "Carla Sosa"));
        ConfigurarCheckIns(
            CheckInDe(rojo, TrafficLight.Red, "Lesión activa"),
            CheckInDe(verde, TrafficLight.Green, "Estado óptimo"));

        var resultado = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.Equal(3, resultado.Count);

        // Rojo primero, con el color y el motivo persistidos en el check-in.
        Assert.Equal(rojo, resultado[0].AthleteId);
        Assert.Equal("Ana Rojas", resultado[0].AthleteName);
        Assert.Equal("Red", resultado[0].TrafficLight);
        Assert.Equal("Lesión activa", resultado[0].TrafficLightReason);
        Assert.True(resultado[0].HasReportedToday);

        // Sin check-in: Amarillo con el motivo del motor, sin fecha de reporte.
        Assert.Equal(sinCheckIn, resultado[1].AthleteId);
        Assert.Equal("Yellow", resultado[1].TrafficLight);
        Assert.Contains("sin check-in", resultado[1].TrafficLightReason!, StringComparison.Ordinal);
        Assert.False(resultado[1].HasReportedToday);
        Assert.Null(resultado[1].ReportedAt);

        // Verde al final.
        Assert.Equal(verde, resultado[2].AthleteId);
        Assert.Equal("Green", resultado[2].TrafficLight);
        Assert.True(resultado[2].HasReportedToday);
    }

    [Fact]
    public async Task HandleAsync_WithReportedAthlete_KeepsPersistedColorAndMapsReportedAt()
    {
        var athleteId = Guid.NewGuid();
        var checkIn = CheckInDe(athleteId, TrafficLight.Yellow, "Señales de alerta: poco sueño");
        ConfigurarAtletas(Atleta(athleteId, "Lucas Fernández"));
        ConfigurarCheckIns(checkIn);

        var resultado = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        var resumen = Assert.Single(resultado);
        Assert.Equal("Yellow", resumen.TrafficLight);
        Assert.Equal("Señales de alerta: poco sueño", resumen.TrafficLightReason);
        Assert.True(resumen.HasReportedToday);
        Assert.Equal(checkIn.CreatedAt, resumen.ReportedAt);
    }

    [Fact]
    public async Task HandleAsync_WithoutCheckIn_UsesEngineWithNullData_YellowSinCheckIn()
    {
        var athleteId = Guid.NewGuid();
        ConfigurarAtletas(Atleta(athleteId, "Sofía Ruiz"));
        ConfigurarCheckIns();

        var resultado = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        var resumen = Assert.Single(resultado);
        Assert.Equal("Yellow", resumen.TrafficLight);
        Assert.Contains("sin check-in", resumen.TrafficLightReason!, StringComparison.Ordinal);
        Assert.False(resumen.HasReportedToday);
    }

    [Fact]
    public async Task HandleAsync_WhenStructureUnavailable_DegradesToTodaysCheckIns()
    {
        // Resiliencia (KAN-364): Estructura caída no puede tumbar la pantalla del PF.
        var registrado = Guid.NewGuid();
        var otro = Guid.NewGuid();
        _structure
            .Setup(client => client.GetCategoryAthletesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new StructureUnavailableException());
        _checkIns
            .Setup(repo => repo.GetByDateAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CheckIn>
            {
                CheckInDe(registrado, TrafficLight.Red, "Lesión activa"),
                CheckInDe(otro, TrafficLight.Green, "Estado óptimo")
            });

        var resultado = await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.Equal(2, resultado.Count);
        Assert.All(resultado, item =>
        {
            Assert.Equal(string.Empty, item.AthleteName);
            Assert.True(item.HasReportedToday);
        });
        Assert.Equal("Red", resultado[0].TrafficLight);
        Assert.Equal("Lesión activa", resultado[0].TrafficLightReason);
        Assert.Equal("Green", resultado[1].TrafficLight);

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
        DateTime? fechaRecibida = null;
        ConfigurarAtletas(Atleta(Guid.NewGuid(), "Ana Rojas"));
        _checkIns
            .Setup(repo => repo.GetByAthletesAndDateAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyCollection<Guid>, DateTime, CancellationToken>((_, fecha, _) => fechaRecibida = fecha)
            .ReturnsAsync(new List<CheckIn>());

        await CreateHandler().HandleAsync(new GetCategoryTrafficLightTodayQuery(_categoryId));

        Assert.NotNull(fechaRecibida);
        Assert.Equal(ArgentinaTime.Today, DateOnly.FromDateTime(fechaRecibida!.Value));
    }
}
