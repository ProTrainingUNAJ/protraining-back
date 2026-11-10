using Application.DTOs.FieldTests;
using Application.Exceptions;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Mappings;
using Domain.Entities;
using Domain.Exceptions;

namespace Application.UseCases.FieldTests.ConfirmFieldTestResult;

// Confirmación de un resultado de fuerza y envío síncrono del 1RM a Entrenamiento:
// 1 SELECT (sesión) + 1 SELECT (resultado) + 1 SELECT (test del catálogo),
// y después la llamada HTTP con la resiliencia global de Polly. Sesión, resultado o
// test ajeno → 404. Test sin mapeo a ejercicio (no fuerza/kg) → 400. Si Entrenamiento
// no responde tras los reintentos → EntrenamientoUnavailableException → 503: la
// confirmación es síncrona y el PF debe ver que el 1RM no se sincronizó.
//
// El mapeo test → ejercicio se resuelve por nombre exacto la primera vez (los ids de
// Entrenamiento no son estables entre entornos) y se cachea en CatalogTest.ExerciseId;
// las confirmaciones siguientes van directo con el GUID cacheado.
public sealed class ConfirmFieldTestResultCommandHandler(
    IFieldTestRepository fieldTestRepository,
    ICatalogTestRepository catalogTestRepository,
    IEntrenamientoClient entrenamientoClient,
    IUnitOfWork unitOfWork)
{
    // Origen "testeado" del contrato de Entrenamiento (RecordSource.Tested).
    private const string TestedOrigin = "TestedInSession";

    public async Task<FieldTestResultDto> HandleAsync(
        ConfirmFieldTestResultCommand command,
        CancellationToken cancellationToken = default)
    {
        // Sesión inexistente o de otro PF (filtro global) → 404.
        var session = await fieldTestRepository.GetSessionByIdAsync(command.SessionId, cancellationToken)
            ?? throw new FieldTestSessionNotFoundException();

        // Sesión cerrada → 400: no se re-confirma ni se dispara un envío tardío de 1RM
        // a Entrenamiento (KAN-427).
        if (session.Status == FieldTestSessionStatus.Closed)
            throw new DomainException("La sesión ya está cerrada.");

        // Resultado inexistente, de otra sesión o de otro PF → 404.
        var result = await fieldTestRepository.GetResultAsync(command.SessionId, command.ResultId, cancellationToken)
            ?? throw new FieldTestResultNotFoundException();

        var catalogTest = await catalogTestRepository.GetByIdAsync(result.CatalogTestId, cancellationToken)
            ?? throw new CatalogTestNotFoundException();

        // Test sin mapeo (no fuerza/kg, p. ej. Carrera 30 m) → 400: no genera récord 1RM.
        if (string.IsNullOrWhiteSpace(catalogTest.TargetExerciseName))
            throw new DomainException("Este test no genera récord 1RM.");

        var exerciseId = catalogTest.ExerciseId;
        if (exerciseId is null)
        {
            // Primera confirmación: se resuelve por nombre exacto en Entrenamiento y el
            // GUID queda cacheado para las siguientes (mismo caso de uso, único SaveChanges).
            exerciseId = await entrenamientoClient.FindExerciseIdByNameAsync(
                    catalogTest.TargetExerciseName, cancellationToken)
                ?? throw new DomainException(
                    $"No se encontró el ejercicio \"{catalogTest.TargetExerciseName}\" en Entrenamiento.");

            catalogTest.CacheExerciseId(exerciseId.Value);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        await entrenamientoClient.SaveRecordAsync(
            result.AthleteId,
            exerciseId.Value,
            result.Value,
            TestedOrigin,
            cancellationToken);

        return FieldTestMappings.ToDto(result);
    }
}
