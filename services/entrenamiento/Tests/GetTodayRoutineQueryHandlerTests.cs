using Application.DTOs.Routines;
using Application.Interfaces.Persistence;
using Application.UseCases.Routines.GetTodayRoutine;
using Domain.Entities;
using Domain.Services;
using Moq;
using System.Reflection;
using Xunit;

namespace Entrenamiento.Tests;

public class GetTodayRoutineQueryHandlerTests
{
    private readonly Mock<ITrainingSessionRepository> _sessionRepoMock = new();
    private readonly Mock<IAthleteRecordRepository> _athleteRecordRepoMock = new();
    private readonly LoadCalculatorEngine _loadCalculatorEngine = new();

    private GetTodayRoutineQueryHandler CreateHandler() =>
        new(_athleteRecordRepoMock.Object, _sessionRepoMock.Object, _loadCalculatorEngine);

    [Fact]
    public async Task Given_AtletaConRecordTesteado100KgYSesionAl80_When_ConsultaRutina_Then_Devuelve80Kg()
    {
        // ARRANGE: Criterio 1: 1RM 100 kg en Sentadilla al 80% -> 80.0 kg
        var athleteId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas", description: "Espalda recta");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 80m);

        // Simulamos que el repositorio de sesiones devuelve la sesión
        _sessionRepoMock
            .Setup(r => r.GetTodaySessionByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Simulamos que el atleta tiene un récord TESTEADO de 100 kg
        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Tested, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        // ACT
        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        // ASSERT
        var item = Assert.Single(result.Exercises);
        Assert.Equal("Sentadilla Trasera", item.Name);
        Assert.Equal(80.0, item.WeightKg);
        Assert.Equal(100.0, item.Current1RmKg);
    }

    [Fact]
    public async Task Given_AtletaConRecordEstimado100KgYSesionAl75_When_ConsultaRutina_Then_Devuelve72Punto5Kg()
    {
        // ARRANGE: Criterio 2: 1RM estimado de 100 kg con escalón conservador y redondeo -> 72.5 kg
        // (100 * 0.75) * 0.95 = 71.25 kg -> redondeado a saltos de 2.5 kg = 72.5 kg
        var athleteId = Guid.NewGuid();
        var exercise = Exercise.Create("Sentadilla Trasera", "Piernas");
        var session = CreateSessionWithExercise(exercise, targetPercentage: 75m);

        _sessionRepoMock
            .Setup(r => r.GetTodaySessionByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(session);

        // Récord ESTIMADO de 100 kg
        var record = AthleteRecord.Create(athleteId, exercise.Id, 100m, RecordSource.Estimated, DateTime.UtcNow);
        _athleteRecordRepoMock
            .Setup(r => r.GetActiveRecordAsync(athleteId, exercise.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(record);

        var handler = CreateHandler();

        // ACT
        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        // ASSERT
        var item = Assert.Single(result.Exercises);
        Assert.Equal(72.5, item.WeightKg);
    }

    [Fact]
    public async Task Given_AtletaSinPlan_When_ConsultaRutina_Then_DevuelveRutinaVacia()
    {
        // ARRANGE: Atleta sin plan asignado
        var athleteId = Guid.NewGuid();
        _sessionRepoMock
            .Setup(r => r.GetTodaySessionByAthleteIdAsync(athleteId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TrainingSession?)null);

        var handler = CreateHandler();

        // ACT
        var result = await handler.Handle(new GetTodayRoutineQuery { AthleteId = athleteId });

        // ASSERT
        Assert.Empty(result.Exercises);
    }

    // Helper para armar la sesión en memoria para los tests
    private static TrainingSession CreateSessionWithExercise(Exercise exercise, decimal targetPercentage)
    {
        var session = TrainingSession.Create(Guid.NewGuid(), "Día 1", 1, DayOfWeek.Monday, "Espalda recta");
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
}
