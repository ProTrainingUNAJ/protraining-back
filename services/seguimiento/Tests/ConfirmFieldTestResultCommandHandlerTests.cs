using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.FieldTests.ConfirmFieldTestResult;
using Domain.Entities;
using Domain.Exceptions;
using Moq;

namespace Seguimiento.Tests;

// La confirmación resuelve el ejercicio de Entrenamiento por nombre exacto la
// primera vez (y cachea el GUID), envía el 1RM con origen TestedInSession, y degrada a
// 404/400/503 según el fallo. No persiste estado de confirmación.
public class ConfirmFieldTestResultCommandHandlerTests
{
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ResultId = Guid.NewGuid();
    private static readonly Guid AthleteId = Guid.NewGuid();
    private static readonly Guid CachedExerciseId = Guid.NewGuid();

    private readonly Mock<IFieldTestRepository> _fieldTests = new();
    private readonly Mock<ICatalogTestRepository> _catalogTests = new();
    private readonly Mock<IEntrenamientoClient> _entrenamiento = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly FieldTestSession _session = new()
    {
        CategoryId = Guid.NewGuid(),
        Date = new DateTime(2026, 11, 4),
        Name = "Evaluación de pretemporada",
        Status = FieldTestSessionStatus.InProgress
    };

    private readonly FieldTestResult _result = new()
    {
        SessionId = SessionId,
        CatalogTestId = Guid.NewGuid(),
        AthleteId = AthleteId,
        Value = 120.0m,
        Attempt = 1,
        RecordedAt = DateTime.UtcNow
    };

    public ConfirmFieldTestResultCommandHandlerTests()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(SessionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_session);

        _fieldTests
            .Setup(repo => repo.GetResultAsync(SessionId, ResultId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_result);

        _entrenamiento
            .Setup(client => client.SaveRecordAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWork
            .Setup(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private ConfirmFieldTestResultCommandHandler CreateHandler() =>
        new(_fieldTests.Object, _catalogTests.Object, _entrenamiento.Object, _unitOfWork.Object);

    private static ConfirmFieldTestResultCommand Command() => new(SessionId, ResultId);

    // Test de fuerza con mapeo y GUID ya cacheado (el caso más común después de la primera).
    private CatalogTest TestConCache() => new(
        "Sentadilla 1RM", MotorCapacity.Strength, "kg", 3,
        targetExerciseName: "Sentadilla con barra");

    private void SetupTest(CatalogTest test) =>
        _catalogTests
            .Setup(repo => repo.GetByIdAsync(_result.CatalogTestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(test);

    [Fact]
    public async Task HandleAsync_WithCachedExerciseId_SendsRecordWithoutResolvingOrSaving()
    {
        var test = TestConCache();
        test.CacheExerciseId(CachedExerciseId);
        SetupTest(test);

        var dto = await CreateHandler().HandleAsync(Command());

        // La respuesta refleja la fila del resultado (id autogenerado por BaseEntity).
        Assert.Equal(_result.Id, dto.Id);
        Assert.Equal(120.0m, dto.Value);

        // Va directo al guardado: ni búsqueda de ejercicio ni escritura en la base.
        _entrenamiento.Verify(
            client => client.FindExerciseIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(AthleteId, CachedExerciseId, 120.0m, "TestedInSession",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // Primera confirmación → resuelve por nombre exacto, cachea el GUID
    // y envía el 1RM testeado (origen "testeado" = TestedInSession).
    [Fact]
    public async Task HandleAsync_WithoutCachedId_ResolvesByNameCachesAndSendsTestedRecord()
    {
        var test = TestConCache();
        SetupTest(test);
        _entrenamiento
            .Setup(client => client.FindExerciseIdByNameAsync("Sentadilla con barra", It.IsAny<CancellationToken>()))
            .ReturnsAsync(CachedExerciseId);

        await CreateHandler().HandleAsync(Command());

        _entrenamiento.Verify(
            client => client.FindExerciseIdByNameAsync("Sentadilla con barra", It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(CachedExerciseId, test.ExerciseId);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(AthleteId, CachedExerciseId, 120.0m, "TestedInSession",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownSession_ThrowsNotFoundWithoutCallingEntrenamiento()
    {
        _fieldTests
            .Setup(repo => repo.GetSessionByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestSession?)null);

        await Assert.ThrowsAsync<FieldTestSessionNotFoundException>(
            () => CreateHandler().HandleAsync(Command()));

        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithUnknownResult_ThrowsNotFoundWithoutCallingEntrenamiento()
    {
        _fieldTests
            .Setup(repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestResult?)null);

        await Assert.ThrowsAsync<FieldTestResultNotFoundException>(
            () => CreateHandler().HandleAsync(Command()));

        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Test sin mapeo (no fuerza/kg, p. ej. Carrera 30 m) → 400, sin llamar a
    // Entrenamiento.
    [Fact]
    public async Task HandleAsync_WithoutTargetExercise_ThrowsDomainExceptionWithoutCallingEntrenamiento()
    {
        SetupTest(new CatalogTest("Carrera 30 m", MotorCapacity.Velocity, "s", 2));

        var error = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command()));

        Assert.Equal("Este test no genera récord 1RM.", error.Message);
        _entrenamiento.Verify(
            client => client.FindExerciseIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Entrenamiento no tiene un ejercicio con ese nombre → 400 (no se cachea nada).
    [Fact]
    public async Task HandleAsync_WhenExerciseNotFoundInEntrenamiento_ThrowsDomainExceptionWithoutSending()
    {
        SetupTest(TestConCache());
        _entrenamiento
            .Setup(client => client.FindExerciseIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command()));

        Assert.Contains("Sentadilla con barra", error.Message);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Entrenamiento caído tras los reintentos de Polly → el error se
    // propaga (503), no se responde 200 como si hubiera sincronizado.
    [Fact]
    public async Task HandleAsync_WhenEntrenamientoUnavailable_Propagates503WithoutSwallowing()
    {
        var test = TestConCache();
        test.CacheExerciseId(CachedExerciseId);
        SetupTest(test);
        _entrenamiento
            .Setup(client => client.SaveRecordAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EntrenamientoUnavailableException());

        await Assert.ThrowsAsync<EntrenamientoUnavailableException>(
            () => CreateHandler().HandleAsync(Command()));
    }

    // El resultado ajeno o de otra sesión nunca llega a Entrenamiento (404).
    [Fact]
    [Trait("Category", "Isolation")]
    public async Task HandleAsync_WithResultOfAnotherSession_ThrowsNotFoundWithoutCallingEntrenamiento()
    {
        _fieldTests
            .Setup(repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldTestResult?)null);

        await Assert.ThrowsAsync<FieldTestResultNotFoundException>(
            () => CreateHandler().HandleAsync(Command()));

        _entrenamiento.Verify(
            client => client.FindExerciseIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // KAN-427: en una sesión cerrada no se re-confirma ni se dispara un envío
    // tardío de 1RM a Entrenamiento → 400 sin tocar nada.
    [Fact]
    public async Task HandleAsync_WithClosedSession_ThrowsDomainExceptionWithoutCallingEntrenamiento()
    {
        _session.Status = FieldTestSessionStatus.Closed;

        var error = await Assert.ThrowsAsync<DomainException>(
            () => CreateHandler().HandleAsync(Command()));

        Assert.Equal("La sesión ya está cerrada.", error.Message);
        _fieldTests.Verify(
            repo => repo.GetResultAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entrenamiento.Verify(
            client => client.SaveRecordAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<decimal>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _unitOfWork.Verify(uow => uow.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
