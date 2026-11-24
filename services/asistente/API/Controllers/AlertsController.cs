using Application.DTOs.Alerts;
using Application.UseCases.Alerts.ApplyAlertAdjustment;
using Application.UseCases.Alerts.CreatePreventiveAlert;
using Application.UseCases.Alerts.GetAlertById;
using Application.UseCases.Alerts.GetAlerts;
using Application.UseCases.Alerts.ReviewAlert;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Centro de Avisos Preventivos con propuestas de ajuste de carga para el Preparador Físico (#179 · KAN-474, #183 · KAN-478).
/// </summary>
[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1/alerts")]
public sealed class AlertsController(
    GetAlertsQueryHandler getAlertsHandler,
    GetAlertByIdQueryHandler getAlertByIdHandler,
    ReviewAlertCommandHandler reviewAlertHandler,
    CreatePreventiveAlertCommandHandler createAlertHandler,
    ApplyAlertAdjustmentCommandHandler applyAlertHandler) : ControllerBase
{
    /// <summary>
    /// Lista los avisos preventivos del PF actual, con filtros por estado de revisión y severidad.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PreventiveAlertResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<PreventiveAlertResponseDto>>> List(
        [FromQuery] bool onlyUnreviewed = true,
        [FromQuery] string? severity = null,
        CancellationToken cancellationToken = default)
    {
        var result = await getAlertsHandler.HandleAsync(new GetAlertsQuery(onlyUnreviewed, severity), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Obtiene el detalle de un aviso preventivo por su identificador.
    /// Retorna 404 si no existe o si pertenece a otro PF (defensa contra enumeración).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PreventiveAlertResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PreventiveAlertResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await getAlertByIdHandler.HandleAsync(new GetAlertByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Registra la revisión humana del preparador físico sobre la propuesta de ajuste.
    /// </summary>
    [HttpPatch("{alertId:guid}/review")]
    [ProducesResponseType(typeof(ReviewAlertResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ReviewAlertResponseDto>> Review(
        Guid alertId,
        CancellationToken cancellationToken)
    {
        var result = await reviewAlertHandler.HandleAsync(new ReviewAlertCommand(alertId), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Aplica el ajuste sugerido por un aviso preventivo en Entrenamiento con un solo clic (#183 · KAN-478).
    /// Con <c>nextSession=true</c> lo aplica a la próxima sesión del atleta en lugar de la de hoy.
    /// </summary>
    [HttpPost("{alertId:guid}/apply")]
    [ProducesResponseType(typeof(ApplyAlertAdjustmentResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApplyAlertAdjustmentResponseDto>> Apply(
        Guid alertId,
        [FromQuery] bool nextSession = false,
        CancellationToken cancellationToken = default)
    {
        var result = await applyAlertHandler.HandleAsync(
            new ApplyAlertAdjustmentCommand(alertId, nextSession),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Crea y persiste un nuevo aviso preventivo con propuesta de ajuste de carga.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PreventiveAlertResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PreventiveAlertResponseDto>> Create(
        [FromBody] CreatePreventiveAlertRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await createAlertHandler.HandleAsync(new CreatePreventiveAlertCommand(request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}

