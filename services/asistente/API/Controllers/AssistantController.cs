using Application.DTOs.Ai;
using Application.DTOs.Adaptation;
using Application.DTOs.DayAdjustments;
using Application.DTOs.DayConflicts;
using Application.DTOs.PfDecisions;
using Application.UseCases.Ai.SuggestRoutine;
using Application.UseCases.Adaptation.GetAthleteAdaptationContext;
using Application.UseCases.DayAdjustments.ApproveDayAdjustment;
using Application.UseCases.DayAdjustments.CreateDayAdjustment;
using Application.UseCases.DayAdjustments.RejectDayAdjustment;
using Application.UseCases.DayConflicts.GetDayConflicts;
using Application.UseCases.PfDecisions.GetAthleteDecisions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1/assistant")]
public sealed class AssistantController(
    SuggestRoutineUseCase suggestRoutineUseCase,
    GetAthleteDecisionsQueryHandler getAthleteDecisionsHandler,
    GetDayConflictsQueryHandler getDayConflictsHandler,
    CreateDayAdjustmentCommandHandler? createDayAdjustmentHandler = null,
    ApproveDayAdjustmentCommandHandler? approveDayAdjustmentHandler = null,
    RejectDayAdjustmentCommandHandler? rejectDayAdjustmentHandler = null,
    GetAthleteAdaptationContextQueryHandler? getAthleteAdaptationContextHandler = null) : ControllerBase
{
    /// <summary>
    /// Genera una propuesta estructurada de rutina consultando directamente al módulo de IA (#086 · KAN-381).
    /// </summary>
    [HttpPost("suggest-routine")]
    [ProducesResponseType(typeof(RoutineSuggestionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<RoutineSuggestionResponseDto>> SuggestRoutine(
        [FromBody] RoutineSuggestionRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await suggestRoutineUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Consulta las decisiones históricas del PF sobre propuestas del asistente (#128 · KAN-423).
    /// Devuelve como máximo las últimas 50 decisiones para el atleta especificado, con filtro opcional por zona.
    /// Responde 404 si el atleta no existe o no pertenece al preparador físico autenticado.
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/decisions")]
    [ProducesResponseType(typeof(IReadOnlyList<PfDecisionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<PfDecisionResponseDto>>> GetAthleteDecisions(
        Guid athleteId,
        [FromQuery] string? zone,
        CancellationToken cancellationToken)
    {
        var result = await getAthleteDecisionsHandler.HandleAsync(
            new GetAthleteDecisionsQuery(athleteId, zone),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Detecta si la rutina de hoy del atleta trabaja zonas donde reportó dolor en el check-in (#156 · KAN-451).
    /// Responde 404 si el atleta no existe o pertenece a otro PF, y 503 si Seguimiento o Entrenamiento no están disponibles.
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/day-conflicts")]
    [ProducesResponseType(typeof(DayConflictsResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DayConflictsResponseDto>> GetDayConflicts(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var result = await getDayConflictsHandler.HandleAsync(
            new GetDayConflictsQuery(athleteId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Reúne lesiones activas, récords vigentes y sustitutos autorizados para preparar una adaptación individual (#164 · KAN-459).
    /// </summary>
    [HttpGet("athletes/{athleteId:guid}/adaptation-context")]
    [ProducesResponseType(typeof(AthleteAdaptationContextDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AthleteAdaptationContextDto>> GetAthleteAdaptationContext(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(getAthleteAdaptationContextHandler);

        var result = await getAthleteAdaptationContextHandler.HandleAsync(
            new GetAthleteAdaptationContextQuery(athleteId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Genera la propuesta de ajuste del día a partir de los conflictos de hoy (#157 · KAN-452).
    /// Si ya existe una propuesta Pending para hoy, devuelve esa sin duplicarla.
    /// </summary>
    [HttpPost("athletes/{athleteId:guid}/day-adjustments")]
    [ProducesResponseType(typeof(DayAdjustmentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DayAdjustmentResponseDto>> CreateDayAdjustment(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createDayAdjustmentHandler);

        var result = await createDayAdjustmentHandler.HandleAsync(
            new CreateDayAdjustmentCommand(athleteId),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Aprueba la propuesta de ajuste del día (pudiendo cambiar el sustituto por otro de la lista de KAN-387)
    /// y aplica el cambio en la sesión del atleta en Entrenamiento (#157 · KAN-452).
    /// </summary>
    [HttpPost("day-adjustments/{id:guid}/approve")]
    [ProducesResponseType(typeof(DayAdjustmentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DayAdjustmentResponseDto>> ApproveDayAdjustment(
        Guid id,
        [FromBody] ApproveDayAdjustmentRequestDto? request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(approveDayAdjustmentHandler);

        var result = await approveDayAdjustmentHandler.HandleAsync(
            new ApproveDayAdjustmentCommand(id, request),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Rechaza la propuesta de ajuste del día dejando la sesión sin cambios (#157 · KAN-452).
    /// </summary>
    [HttpPost("day-adjustments/{id:guid}/reject")]
    [ProducesResponseType(typeof(DayAdjustmentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DayAdjustmentResponseDto>> RejectDayAdjustment(
        Guid id,
        [FromBody] RejectDayAdjustmentRequestDto? request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejectDayAdjustmentHandler);

        var result = await rejectDayAdjustmentHandler.HandleAsync(
            new RejectDayAdjustmentCommand(id, request),
            cancellationToken);

        return Ok(result);
    }
}
