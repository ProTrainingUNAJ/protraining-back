using Application.DTOs.TrainingPlans;
using Application.UseCases.TrainingPlans.ApplyTemplate;
using Application.UseCases.TrainingPlans.AssignPlan;
using Application.UseCases.TrainingPlans.CreatePlan;
using Application.UseCases.TrainingPlans.GetPlan;
using Application.UseCases.TrainingPlans.GetSchedule;
using Application.UseCases.TrainingPlans.ListPlans;
using Application.UseCases.TrainingPlans.ListTemplates;
using Application.UseCases.TrainingPlans.SavePlanAsTemplate;
using Application.UseCases.TrainingPlans.UpdatePlan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace API.Controllers;

[Route("api/v1/training-plans")]
[ApiController]
[Authorize]
public class TrainingPlanController : ControllerBase
{
    private readonly AssignPlanCommandHandler _assignPlanHandler;
    private readonly CreateTrainingPlanCommandHandler _createPlanHandler;
    private readonly UpdateTrainingPlanCommandHandler _updatePlanHandler;
    private readonly GetTrainingPlanQueryHandler _getPlanHandler;
    private readonly ListTrainingPlansQueryHandler _listPlansHandler;
    private readonly GetPlanScheduleQueryHandler _scheduleHandler;
    private readonly SavePlanAsTemplateCommandHandler _savePlanAsTemplateHandler;
    private readonly ListPlanTemplatesQueryHandler _listPlansTemplatesHandler;
    private readonly ApplyPlanTemplateCommandHandler _applyPlanTemplateHandler;

    public TrainingPlanController(
        AssignPlanCommandHandler assignPlanHandler,
        CreateTrainingPlanCommandHandler createPlanHandler,
        UpdateTrainingPlanCommandHandler updatePlanHandler,
        GetTrainingPlanQueryHandler getPlanHandler,
        ListTrainingPlansQueryHandler listPlansHandler,
        GetPlanScheduleQueryHandler scheduleHandler,
        SavePlanAsTemplateCommandHandler savePlanAsTemplateHandler,
        ListPlanTemplatesQueryHandler listPlanTemplatesHandler,
        ApplyPlanTemplateCommandHandler applyPlanTemplateHandler)
    {
        _assignPlanHandler = assignPlanHandler;
        _createPlanHandler = createPlanHandler;
        _updatePlanHandler = updatePlanHandler;
        _getPlanHandler = getPlanHandler;
        _listPlansHandler = listPlansHandler;
        _scheduleHandler = scheduleHandler;
        _savePlanAsTemplateHandler = savePlanAsTemplateHandler;
        _listPlansTemplatesHandler = listPlanTemplatesHandler;
        _applyPlanTemplateHandler = applyPlanTemplateHandler;
    }

    [HttpPost]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingPlanDetailDto>> CreatePlan([FromBody] SaveTrainingPlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await _createPlanHandler.Handle(new CreateTrainingPlanCommand(request), cancellationToken);
        return Created($"/api/v1/training-plans/{plan.Id}", plan);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingPlanDetailDto>> GetPlan(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _getPlanHandler.Handle(new GetTrainingPlanQuery(id), cancellationToken);
        return Ok(plan);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingPlanDetailDto>> UpdatePlan(Guid id, [FromBody] SaveTrainingPlanRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await _updatePlanHandler.Handle(new UpdateTrainingPlanCommand(id, request), cancellationToken);
        return Ok(plan);
    }

    [HttpGet]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<List<TrainingPlanSummaryDto>>> ListPlans(CancellationToken cancellationToken = default)
    {
        var plans = await _listPlansHandler.Handle(new ListTrainingPlansQuery(), cancellationToken);
        return Ok(plans);
    }

    // Agenda del PF para el calendario: las sesiones que tocan en una fecha en todas sus asignaciones
    [HttpGet("schedule")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<List<ScheduledSessionDto>>> GetSchedule([FromQuery, BindRequired] DateOnly date, CancellationToken cancellationToken = default)
    {
        var sessions = await _scheduleHandler.Handle(new GetPlanScheduleQuery(date), cancellationToken);
        return Ok(sessions);
    }

    [HttpPost("assignments")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> AssignPlan([FromBody] AssignPlanRequest request, CancellationToken cancellationToken = default)
    {
        var command = new AssignPlanCommand(
            request.TrainingPlanId,
            request.StartDate,
            request.CategoryId,
            request.AthleteId
        );

        var assignmentId = await _assignPlanHandler.Handle(command, cancellationToken);
        return Created($"/api/v1/training-plans/assignments/{assignmentId}", new { assignmentId, message = "Plan asignado exitosamente." });
    }

    [HttpPost("{id:guid}/save-as-template")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingPlanDetailDto>> SaveAsTemplate(Guid id, [FromBody] SavePlanAsTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var plan = await _savePlanAsTemplateHandler.Handle(new SavePlanAsTemplateCommand(id, request.Name), cancellationToken);

        return Created($"/api/v1/training-plans/{plan.Id}", plan);
    }

    [HttpGet("templates")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<List<TrainingPlanSummaryDto>>> ListTemplates(CancellationToken cancellationToken = default)
    {
        var templates = await _listPlansTemplatesHandler.Handle( new ListPlanTemplatesQuery(), cancellationToken);

        return Ok(templates);
    }

    [HttpPost("templates/{id:guid}/apply")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> ApplyTemplate(Guid id, [FromBody] ApplyPlanTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var command = new ApplyPlanTemplateCommand(
            id,
            request.StartDate,
            request.CategoryId,
            request.AthleteId);

        var assignmentId = await _applyPlanTemplateHandler.Handle(command, cancellationToken);

        return Created($"/api/v1/training-plans/assignments/{assignmentId}", new { assignmentId, message = "Plantilla aplicada y asignada exitosamente." });
    }
}
