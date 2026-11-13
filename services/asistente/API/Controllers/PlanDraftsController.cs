using Application.DTOs.PlanDrafts;
using Application.UseCases.PlanDrafts.ApprovePlanDraft;
using Application.UseCases.PlanDrafts.CreatePlanDraft;
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
    ApprovePlanDraftCommandHandler approvePlanDraftHandler,
    DiscardPlanDraftCommandHandler discardPlanDraftHandler,
    CreatePlanDraftCommandHandler? createPlanDraftHandler = null) : ControllerBase
{
    /// <summary>
    /// Genera y persiste un nuevo borrador de planificación multisesión asistido por IA (KAN-381 / KAN-383).
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PlanDraftResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlanDraftResponseDto>> Create(
        [FromBody] CreatePlanDraftRequestDto request,
        CancellationToken cancellationToken)
    {
        if (createPlanDraftHandler == null)
            throw new InvalidOperationException("CreatePlanDraftCommandHandler no está configurado.");

        var result = await createPlanDraftHandler.HandleAsync(new CreatePlanDraftCommand(request), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

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
    /// Aprueba el borrador de planificación, creándolo y asignándolo en el microservicio de Entrenamiento (#116 · KAN-411).
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(ApprovePlanDraftResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApprovePlanDraftResponseDto>> Approve(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await approvePlanDraftHandler.HandleAsync(new ApprovePlanDraftCommand(id), cancellationToken);
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
