using Application.DTOs.Attendance;
using Application.UseCases.Attendance.SetAttendance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Attendances;

// Corrección manual de asistencia por parte del PF (KAN-445).
[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/attendance")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class AttendancesController(
    SetAttendanceCommandHandler handler) : ControllerBase
{
    [HttpPatch]
    [ProducesResponseType(typeof(ProblemDetails), 204)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> Set(
        Guid athleteId, SetAttendanceRequest request, CancellationToken cancellationToken)
    {
        await handler.HandleAsync(new SetAttendanceCommand(athleteId, request), cancellationToken);
        return NoContent();
    }
}