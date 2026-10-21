using Application.DTOs.Memberships;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Routines.GetTodayRoutine;
using Domain.Entities;
using Domain.Services;
using Moq;
using System.Reflection;

namespace Entrenamiento.Tests;

public class GetTodayRoutineQueryHandlerTests
{
    private readonly Mock<ITrainingSessionRepository> _sessionRepoMock = new();
    private readonly Mock<IAthleteRecordRepository> _athleteRecordRepoMock = new();
    private readonly Mock<IStructureClient> _structureClientMock = new();
    private readonly LoadCalculatorEngine _loadCalculatorEngine = new();

    public GetTodayRoutineQueryHandlerTests()
    {
        // Por defecto el atleta existe; el test que prueba 404 lo sobreescribe
        _structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new StructureAthleteDto(id, "Lucas Gómez"));
    }

    private GetTodayRoutineQueryHandler CreateHandler()
    {
        return new(_athleteRecordRepoMock.Object, _sessionRepoMock.Object, _loadCalculatorEngine, _structureClientMock.Object);
    }

    [Fact]
    public async Task Given_AtletaConRecordTesteado100KgYSesionAl80_When_ConsultaRutina_Then_Devuelve80KgYNotaDelPf()
    {
        // Criterio 1: 1RM 100 kg en Sentadilla al 80% -> 80.0 kg y la nota técnica del PF
        var athleteId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas", description: "Espalda recta");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m, coachNotes: "Controlar técnica en descenso");

        // Simulamos que el repositorio de sesiones devuelve la sesión
        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Simulamos que el atleta tiene un récord TESTEADO de 100 kg
        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        Assert.Equal("Controlar técnica en descenso", result.CoachNotes);
        var item = Assert.Single(result.Exercises);
        Assert.Equal("Sentadilla Trasera", item.Name);
        Assert.Equal(80.0, item.WeightKg);
        Assert.Equal(100.0, item.Current1RmKg);
    }

    [Fact]
    public async Task Given_AtletaConRecordEstimado100KgYSesionAl80_When_ConsultaRutina_Then_Devuelve72Punto5Kg()
    {
        // Criterio 2: 1RM estimado de 100 kg con factor 0.90 (KAN-359) al 80%
        // (100 * 0.80) * 0.90 = 72.0 kg -> redondeado a saltos de 2.5 kg = 72.5 kg
        var athleteId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m);

        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Récord ESTIMADO de 100 kg
        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Estimated, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        var item = Assert.Single(result.Exercises);
        Assert.Equal(72.5, item.WeightKg);
        Assert.False(item.IsReference);
    }

    [Fact]
    public async Task Given_AtletaSinRecord_When_ConsultaRutina_Then_DevuelveWeightKgNullEIsReferenceTrue()
    {
        var athleteId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m);

        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Sin récord activo para este ejercicio
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AthleteRecord?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        var item = Assert.Single(result.Exercises);
        Assert.Null(item.WeightKg);
        Assert.Null(item.Current1RmKg);
        Assert.True(item.IsReference);
    }

    [Fact]
    public async Task Given_AtletaConPlanAsignadoPorCategoria_When_ConsultaRutina_Then_PasaCategoriasARepositorioYDevuelveRutina()
    {
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m);

        _structureClientMock
            .Setup(c => c.GetMeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AthleteMeDto
            { 
                AthleteId = athleteId,
                FirstName = "Lucas",
                LastName = "Gomez",
                FullName = "Lucas Gomez",
                Active = true,
                Categories = [new AthleteCategoryDto(categoryId, Guid.NewGuid(), "Plantel Superior", null)]
            });

        // El repositorio de sesiones recibe el categoryId y devuelve la sesión
        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(
                athleteId,
                It.Is<IReadOnlyList<Guid>>(cats => cats.Contains(categoryId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);

        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery());

        Assert.Equal("Lucas Gomez", result.AthleteName);
        Assert.Equal(athleteId, result.AthleteId);
        Assert.Single(result.Exercises);
    }

    [Fact]
    public async Task Given_AtletaEnDiaDeDescanso_When_ConsultaRutina_Then_DevuelveRutinaVaciaConFechaDeHoy()
    {
        var athleteId = Guid.NewGuid();

        // En día de descanso el repositorio no encuentra sesión para el día de hoy
        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingSession?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        Assert.Empty(result.Exercises);
        Assert.Equal(Domain.Common.ArgentinaTime.Today, result.Date);
    }

    [Fact]
    public async Task Given_AtletaConPlanQueNoEmpezo_When_ConsultaRutina_Then_DevuelveRutinaVacia()
    {
        var athleteId = Guid.NewGuid();

        // Si el plan no comenzó aún, el repositorio retorna null
        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(athleteId, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingSession?)null);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        Assert.Empty(result.Exercises);
    }

    // Helper para armar la sesión en memoria para los tests
    private static TrainingSession CreateSessionWithExercise(Exercise exercise, decimal targetPercentage, string? coachNotes = "Espalda recta")
    {
        var session = TrainingSession.Create(Guid.NewGuid(), "Día 1", 1, DayOfWeek.Monday, coachNotes);
        var prescribed = PrescribedExercise.Create(1, 4, 6, targetPercentage, session.Id, exercise.Id);

        // Conectamos el Exercise navegable invocando el setter privado
        typeof(PrescribedExercise).GetProperty(nameof(PrescribedExercise.Exercise))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(prescribed, new object[] { exercise });

        // Agregamos a la colección privada de la sesión
        var list = (List<PrescribedExercise>)typeof(TrainingSession)
            .GetField("_prescribedExercises", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(session)!;
        list.Add(prescribed);

        return session;
    }

    [Fact]
    public async Task Given_PreparadorFisicoPideRutinaDeAtletaConPlanAsignadoPorCategoria_When_ConsultaRutina_Then_DevuelveNombreDeAtletaYEjercicios()
    {
        var athleteId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m, coachNotes: "Controlar técnica en descenso");

        _structureClientMock
            .Setup(c => c.GetAthleteCategoriesAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AthleteCategoryDto(categoryId, Guid.NewGuid(), "Primera", null)]);

        _structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StructureAthleteDto(athleteId, "Lucas Gómez"));

        _sessionRepoMock
            .Setup(r => r.GetByAthleteIdAsync(
                athleteId,
                It.Is<IReadOnlyList<Guid>>(cats => cats.Contains(categoryId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        Assert.Equal("Lucas Gómez", result.AthleteName);
        Assert.Equal(athleteId, result.AthleteId);
        Assert.Equal("Controlar técnica en descenso", result.CoachNotes);
        var item = Assert.Single(result.Exercises);
        Assert.Equal("Sentadilla Trasera", item.Name);
        Assert.Equal(80.0, item.WeightKg);
    }

    [Fact]
    public async Task Given_PreparadorFisicoPideRutinaDeAtletaQueNoExisteONoEsSuyo_When_ConsultaRutina_Then_LanzaNotFoundException()
    {
        // Arrange
        var athleteId = Guid.NewGuid();

        // Estructura responde null (404)
        _structureClientMock
            .Setup(c => c.GetAthleteByIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StructureAthleteDto?)null);

        var handler = CreateHandler();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId }));

        Assert.Equal("El atleta no existe o no es tuyo", ex.Message);
    }
}
