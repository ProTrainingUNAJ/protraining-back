using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Application.DTOs;
using ProTraining.Common;
using ProTraining.Domain.Entities;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class TrackingController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public TrackingController(IAppDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// CU-07: Completar el check-in diario en menos de 30 segundos
    /// </summary>
    [HttpPost("check-in")]
    public async Task<ActionResult> SubmitCheckIn([FromBody] DailyCheckInRequest request)
    {
        if (request.EffortRate < 1 || request.EffortRate > 10)
            throw new DomainException("El esfuerzo percibido (RPE) debe estar entre 1 y 10.");

        if (request.SleepHours < 0 || request.SleepHours > 24)
            throw new DomainException("Las horas de sueño deben estar entre 0 y 24.");

        if (request.MoodRate < 1 || request.MoodRate > 5)
            throw new DomainException("El estado de ánimo debe estar entre 1 y 5.");

        var athlete = await _db.Athletes.FirstOrDefaultAsync(a => a.Id == request.AthleteId);
        if (athlete == null)
            throw new NotFoundException("Atleta no encontrado.");

        var today = DateTime.UtcNow.Date;
        var existingCheckIn = await _db.DailyCheckIns
            .FirstOrDefaultAsync(c => c.AthleteId == request.AthleteId && c.Date == today);

        if (existingCheckIn != null)
        {
            // Actualizar check-in si ya se había cargado hoy
            existingCheckIn.EffortRate = request.EffortRate;
            existingCheckIn.SleepHours = request.SleepHours;
            existingCheckIn.MoodRate = request.MoodRate;
            existingCheckIn.PainZones = request.PainZones?.Trim() ?? string.Empty;
        }
        else
        {
            var checkIn = new DailyCheckIn
            {
                AccountId = athlete.AccountId,
                AthleteId = athlete.Id,
                Date = today,
                EffortRate = request.EffortRate,
                SleepHours = request.SleepHours,
                MoodRate = request.MoodRate,
                PainZones = request.PainZones?.Trim() ?? string.Empty
            };
            _db.DailyCheckIns.Add(checkIn);
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Check-in diario completado con éxito." });
    }

    [HttpGet("check-ins/{athleteId}")]
    public async Task<ActionResult<List<DailyCheckInDto>>> GetCheckIns(Guid athleteId)
    {
        var checkIns = await _db.DailyCheckIns
            .Where(c => c.AthleteId == athleteId)
            .OrderByDescending(c => c.Date)
            .Take(14)
            .Select(c => new DailyCheckInDto(
                c.Id,
                c.AthleteId,
                c.Date,
                c.EffortRate,
                c.SleepHours,
                c.MoodRate,
                c.PainZones
            ))
            .ToListAsync();

        return Ok(checkIns);
    }

    /// <summary>
    /// CU-08: Registrar y seguir una lesión activa
    /// </summary>
    [HttpPost("injuries")]
    public async Task<ActionResult<InjuryDto>> CreateInjury([FromBody] CreateInjuryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BodyZone))
            throw new DomainException("La zona del cuerpo afectada es obligatoria.");

        var athlete = await _db.Athletes.FirstOrDefaultAsync(a => a.Id == request.AthleteId);
        if (athlete == null)
            throw new NotFoundException("Atleta no encontrado.");

        var injury = new Injury
        {
            AccountId = athlete.AccountId,
            AthleteId = athlete.Id,
            BodyZone = request.BodyZone.Trim(),
            Notes = request.Notes?.Trim() ?? string.Empty
        };

        _db.Injuries.Add(injury);
        await _db.SaveChangesAsync();

        return Ok(new InjuryDto(
            injury.Id,
            injury.AthleteId,
            injury.BodyZone,
            injury.Notes,
            injury.Status,
            injury.CreatedAt
        ));
    }

    /// <summary>
    /// CU-08: Transición de estado de lesión (Activa -> En recuperación -> Alta)
    /// </summary>
    [HttpPatch("injuries/{injuryId}/status")]
    public async Task<ActionResult> UpdateInjuryStatus(Guid injuryId, [FromBody] UpdateInjuryStatusRequest request)
    {
        var injury = await _db.Injuries.FirstOrDefaultAsync(i => i.Id == injuryId);
        if (injury == null)
            throw new NotFoundException("Lesión no encontrada.");

        // Valida regla de negocio: no pasar de Activa a Alta sin pasar por EnRecuperacion
        injury.TransitionTo(request.NewStatus, request.TransitionNote ?? "Cambio de estado");

        await _db.SaveChangesAsync();
        return Ok(new { message = $"Estado de lesión actualizado a {injury.Status}." });
    }

    [HttpGet("injuries/{athleteId}")]
    public async Task<ActionResult<List<InjuryDto>>> GetInjuries(Guid athleteId)
    {
        var injuries = await _db.Injuries
            .Where(i => i.AthleteId == athleteId)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new InjuryDto(
                i.Id,
                i.AthleteId,
                i.BodyZone,
                i.Notes,
                i.Status,
                i.CreatedAt
            ))
            .ToListAsync();

        return Ok(injuries);
    }
}
