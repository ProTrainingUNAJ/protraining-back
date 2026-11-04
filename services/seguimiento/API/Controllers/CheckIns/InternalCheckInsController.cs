using Application.DTOs.CheckIns;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Endpoints internos entre microservicios (Seguimiento -> Asistente / Estructura).
/// Exige rol 'Servicio' (#177 · KAN-472).
/// </summary>
[Authorize(Roles = "Servicio")]
[ApiController]
[Route("api/v1/internal/check-ins")]
public class InternalCheckInsController(ICheckInRepository checkInRepository) : ControllerBase
{
    [HttpPost("done")]
    [ProducesResponseType(typeof(CheckInsDoneResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CheckInsDoneResponse>> GetCompletedCheckIns(
        [FromBody] CheckInsDoneRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PfId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Parámetros inválidos",
                Detail = "El parámetro 'pfId' es obligatorio."
            });
        }

        var completedAthleteIds = await checkInRepository.GetCompletedCheckInAthleteIdsAsync(
            request.Date,
            request.PfId,
            request.AthleteIds,
            cancellationToken);

        return Ok(new CheckInsDoneResponse(completedAthleteIds));
    }

    /// <summary>
    /// Consulta el historial de check-ins en un rango de fechas para el worker nocturno de sobrecarga (#177 · KAN-472).
    /// </summary>
    [HttpPost("history")]
    [ProducesResponseType(typeof(IReadOnlyList<InternalCheckInHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<InternalCheckInHistoryDto>>> GetCheckInsHistory(
        [FromBody] CheckInsHistoryRequest request,
        CancellationToken cancellationToken)
    {
        if (request.AthleteIds.Count == 0)
        {
            return Ok(Array.Empty<InternalCheckInHistoryDto>());
        }

        var startDateTime = DateTime.SpecifyKind(request.StartDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endDateTime = DateTime.SpecifyKind(request.EndDate.ToDateTime(new TimeOnly(23, 59, 59)), DateTimeKind.Utc);

        var checkIns = await checkInRepository.GetHistoryByAthletesAndDateRangeAsync(
            request.AthleteIds,
            startDateTime,
            endDateTime,
            cancellationToken);

        var dtos = checkIns.Select(c => new InternalCheckInHistoryDto(
            c.Id,
            c.AthleteId,
            DateOnly.FromDateTime(c.Date),
            c.SleepHours,
            c.SleepQuality,
            c.MuscleSoreness,
            c.FatigueLevel,
            c.MoodRate,
            c.StressLevel,
            c.EffortRate,
            c.IsSick,
            c.PainZones,
            c.HasActiveInjury
        )).ToList();

        return Ok(dtos);
    }
}
