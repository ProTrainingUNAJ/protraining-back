using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProTraining.Application.Common;
using ProTraining.Application.DTOs;
using ProTraining.Common;
using ProTraining.Domain.Entities;
using ProTraining.Infrastructure.Background;

namespace ProTraining.WebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class AlertsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AlertsBackgroundService _worker;

    public AlertsController(IAppDbContext db, IHostedService worker)
    {
        _db = db;
        _worker = (AlertsBackgroundService)worker;
    }

    [HttpGet]
    public async Task<ActionResult<List<PreventiveAlertDto>>> GetAlerts()
    {
        var alerts = await _db.PreventiveAlerts
            .Include(a => a.Athlete)
            .OrderByDescending(a => a.DateGenerated)
            .Select(a => new PreventiveAlertDto(
                a.Id,
                a.AthleteId,
                $"{a.Athlete!.FirstName} {a.Athlete.LastName}",
                a.DateGenerated,
                a.Severity,
                a.Reason,
                a.Suggestion,
                a.IsReviewed
            ))
            .ToListAsync();

        return Ok(alerts);
    }

    [HttpPatch("{alertId}/review")]
    public async Task<ActionResult> ReviewAlert(Guid alertId)
    {
        var alert = await _db.PreventiveAlerts.FirstOrDefaultAsync(a => a.Id == alertId);
        if (alert == null)
            throw new NotFoundException("Alerta no encontrada.");

        alert.IsReviewed = true;
        await _db.SaveChangesAsync();

        return Ok(new { message = "Alerta marcada como revisada." });
    }

    [HttpPost("trigger-scan")]
    public async Task<ActionResult> TriggerScan()
    {
        await _worker.RunDailyAnalysisAsync();
        return Ok(new { message = "Escaneo preventivo ejecutado correctamente." });
    }
}
