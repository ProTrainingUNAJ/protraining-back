using Application.DTOs.PlanDrafts;
using Application.UseCases.PlanDrafts.DiscardPlanDraft;
using Application.UseCases.PlanDrafts.GetPlanDraft;
using Application.UseCases.PlanDrafts.UpdatePlanDraft;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Authorize(Roles = "PreparadorFisico")]
[ApiController]
[Route("api/v1/assistant/plan-drafts")]
public sealed class PlanDraftsController(
    GetPlanDraftQueryHandler getPlanDraftHandler,
    UpdatePlanDraftCommandHandler updatePlanDraftHandler,
    DiscardPlanDraftCommandHandler discardPlanDraftHandler) : ControllerBase
{
    /// <summary>
    /// Obtiene un borrador de planificación por su identificador (KAN-383 / KAN-410).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PlanDraftResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlanDraftResponseDto>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await getPlanDraftHandler.HandleAsync(new GetPlanDraftQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Reemplaza el contenido entero del borrador de planificación, validando esquema, catálogo,
    /// rangos, cargas pre-partido y registrando cambios de ejercicios en la memoria del PF (#115 · KAN-410).
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PlanDraftResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlanDraftResponseDto>> Update(
        Guid id,
        [FromBody] UpdatePlanDraftRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await updatePlanDraftHandler.HandleAsync(new UpdatePlanDraftCommand(id, request), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Descarta un borrador de planificación dejándolo en estado Discarded (#115 · KAN-410).
    /// </summary>
    [HttpPost("{id:guid}/discard")]
    [ProducesResponseType(typeof(PlanDraftResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlanDraftResponseDto>> Discard(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await discardPlanDraftHandler.HandleAsync(new DiscardPlanDraftCommand(id), cancellationToken);
        return Ok(result);
    }
}
