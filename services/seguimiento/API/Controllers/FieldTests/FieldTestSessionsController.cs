using Application.DTOs.FieldTests;
using Application.UseCases.FieldTests.ConfirmFieldTestResult;
using Application.UseCases.FieldTests.CreateFieldTestResult;
using Application.UseCases.FieldTests.GetFieldTestSession;
using Application.UseCases.FieldTests.UpdateFieldTestResult;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.FieldTests;

// Sesiones de test de campo con carga rápida de resultados (KAN-425): el rol
// PreparadorFisico se valida en este servicio, como el resto de los datos del PF.
// El aislamiento por pf_id lo garantiza el filtro global del AppDbContext: sesión o
// resultado de otro PF → 404, sin revelar que el id exista.
[ApiController]
[Route("api/v1/field-test-sessions")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class FieldTestSessionsController(
    CreateFieldTestResultCommandHandler createHandler,
    GetFieldTestSessionQueryHandler getHandler,
    UpdateFieldTestResultCommandHandler updateHandler,
    ConfirmFieldTestResultCommandHandler confirmHandler) : ControllerBase
{
    // Sesión con sus tests y resultados (contrato: getFieldTestSession): la pantalla
    // del móvil se pinta con un solo GET.
    [HttpGet("{sessionId:guid}")]
    [ProducesResponseType(typeof(FieldTestSessionDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<FieldTestSessionDto>> Get(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await getHandler.HandleAsync(
            new GetFieldTestSessionQuery(sessionId),
            cancellationToken);

        return Ok(session);
    }

    // Carga rápida de un resultado (contrato: createFieldTestResult): 201 con la fila
    // creada; 404 si la sesión o el atleta no son del PF y 409 si el mismo intento ya
    // estaba cargado.
    [HttpPost("{sessionId:guid}/results")]
    [ProducesResponseType(typeof(FieldTestResultDto), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<FieldTestResultDto>> CreateResult(
        Guid sessionId,
        CreateFieldTestResultRequest request,
        CancellationToken cancellationToken = default)
    {
        // Los ! están garantizados por el [Required] del contrato (400 automático).
        var result = await createHandler.HandleAsync(
            new CreateFieldTestResultCommand(
                sessionId,
                request.CatalogTestId!.Value,
                request.AthleteId!.Value,
                request.Value!.Value,
                request.Attempt),
            cancellationToken);

        return Created($"/api/v1/field-test-sessions/{sessionId}/results/{result.Id}", result);
    }

    // Corrección de un resultado ya cargado (contrato: updateFieldTestResult): 200 con
    // el resultado corregido; 404 si la sesión o el resultado no son del PF.
    [HttpPut("{sessionId:guid}/results/{resultId:guid}")]
    [ProducesResponseType(typeof(FieldTestResultDto), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 409)]
    public async Task<ActionResult<FieldTestResultDto>> UpdateResult(
        Guid sessionId,
        Guid resultId,
        UpdateFieldTestResultRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await updateHandler.HandleAsync(
            new UpdateFieldTestResultCommand(
                sessionId,
                resultId,
                request.Value!.Value,
                request.Attempt),
            cancellationToken);

        return Ok(result);
    }

    // Confirmación de un resultado de fuerza (contrato: confirmFieldTestResult): resuelve
    // el ejercicio de Entrenamiento (con caché del GUID) y envía el 1RM con origen
    // "testeado". Síncrono: 200 si Entrenamiento lo guardó; 400 si el test no genera
    // récord 1RM; 404 si la sesión, el resultado o la prueba no son del PF; 503 si
    // Entrenamiento no responde tras los reintentos de Polly.
    [HttpPost("{sessionId:guid}/results/{resultId:guid}/confirm")]
    [ProducesResponseType(typeof(FieldTestResultDto), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    [ProducesResponseType(typeof(ProblemDetails), 503)]
    public async Task<ActionResult<FieldTestResultDto>> ConfirmResult(
        Guid sessionId,
        Guid resultId,
        CancellationToken cancellationToken = default)
    {
        var result = await confirmHandler.HandleAsync(
            new ConfirmFieldTestResultCommand(sessionId, resultId),
            cancellationToken);

        return Ok(result);
    }
}
