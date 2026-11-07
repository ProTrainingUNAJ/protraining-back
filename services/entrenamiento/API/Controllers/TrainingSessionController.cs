using System.Text.Json;
using Application.DTOs.TrainingSessions;
using Application.Interfaces.Persistence;
using Application.UseCases.TrainingSessions.ApplyAthleteAdjustment;
using Application.UseCases.TrainingSessions.GetTrainingSession;
using Application.UseCases.TrainingSessions.UpdateCoachNotes;
using Domain.Common;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/training-sessions")]
[ApiController]
[Authorize]
public class TrainingSessionController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly UpdateCoachNotesCommandHandler _updateCoachNotesHandler;
    private readonly GetTrainingSessionQueryHandler _getSessionHandler;
    private readonly ApplyAthleteSessionAdjustmentCommandHandler? _applyAdjustmentHandler;
    private readonly IAthleteSessionAdjustmentRepository? _adjustmentRepository;

    public TrainingSessionController(
        UpdateCoachNotesCommandHandler updateCoachNotesHandler,
        GetTrainingSessionQueryHandler getSessionHandler,
        ApplyAthleteSessionAdjustmentCommandHandler? applyAdjustmentHandler = null,
        IAthleteSessionAdjustmentRepository? adjustmentRepository = null)
    {
        _updateCoachNotesHandler = updateCoachNotesHandler;
        _getSessionHandler = getSessionHandler;
        _applyAdjustmentHandler = applyAdjustmentHandler;
        _adjustmentRepository = adjustmentRepository;
    }

    // Solo PF (KAN-450): una sesión puede ser de la planificación de otro atleta. El atleta ve
    // la suya, con las notas del PF, en GET /api/v1/me/today-routine.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<TrainingSessionDto>> GetSession(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _getSessionHandler.Handle(new GetTrainingSessionQuery(id), cancellationToken);
        if (session == null)
            return NotFound();

        return Ok(session);
    }

    [HttpPut("{id:guid}/notes")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<IActionResult> UpdateNotes(Guid id, [FromBody] UpdateCoachNotesRequest request, CancellationToken cancellationToken = default)
    {
        var command = new UpdateCoachNotesCommand(id, request.CoachNotes);
        await _updateCoachNotesHandler.Handle(command, cancellationToken);
        return NoContent();
    }

    // Consultar el ajuste o variante de sesión para un atleta y fecha (#168 · KAN-463)
    [HttpGet("{sessionId:guid}/athlete-adjustments")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<AthleteSessionAdjustmentResponseDto>> GetAthleteAdjustment(
        Guid sessionId,
        [FromQuery] Guid athleteId,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken = default)
    {
        if (_adjustmentRepository is null)
            throw new InvalidOperationException("IAthleteSessionAdjustmentRepository no está configurado.");

        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        var targetDate = date ?? ArgentinaTime.Today;
        var adjustment = await _adjustmentRepository.GetBySessionAthleteAndDateAsNoTrackingAsync(
            sessionId,
            athleteId,
            targetDate,
            cancellationToken);

        if (adjustment is null)
            return NotFound();

        var items = JsonSerializer.Deserialize<List<AthleteSessionAdjustmentItemDto>>(adjustment.Items, JsonOptions) ?? [];

        return Ok(new AthleteSessionAdjustmentResponseDto(
            Id: adjustment.Id,
            SessionId: adjustment.TrainingSessionId,
            AthleteId: adjustment.AthleteId,
            Date: adjustment.Date,
            Note: adjustment.Note,
            Items: items,
            TrainingPlanId: adjustment.TrainingPlanId != Guid.Empty ? adjustment.TrainingPlanId : null));
    }

    // Aplicarlo en la rutina del atleta al aprobar el ajuste del día (KAN-452, KAN-463)
    [HttpPost("{sessionId:guid}/athlete-adjustments")]
    [Authorize(Roles = "PreparadorFisico")]
    public async Task<ActionResult<AthleteSessionAdjustmentResponseDto>> ApplyAthleteAdjustments(
        Guid sessionId,
        [FromBody] ApplyAthleteSessionAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_applyAdjustmentHandler is null)
            throw new InvalidOperationException("ApplyAthleteSessionAdjustmentCommandHandler no está configurado.");

        var result = await _applyAdjustmentHandler.Handle(
            new ApplyAthleteSessionAdjustmentCommand(sessionId, request),
            cancellationToken);

        return Ok(result);
    }
}
