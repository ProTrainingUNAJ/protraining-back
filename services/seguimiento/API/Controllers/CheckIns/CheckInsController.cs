using Application.DTOs.CheckIns;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace API.Controllers.CheckIns;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/check-in")]
[Authorize]
public sealed class CheckInsController(SubmitCheckInCommandHandler handler) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CheckInResponseDto), 200)]
    [ProducesResponseType(typeof(CheckInResponseDto), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<CheckInResponseDto>> Submit(
        Guid athleteId,
        SubmitCheckInRequest request,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(
            new SubmitCheckInCommand(
                athleteId,
                request.SleepHours,
                request.SleepQuality,
                request.MuscleSoreness,
                request.FatigueLevel,
                request.MoodRate,
                request.StressLevel,
                request.IsSick,
                request.EffortRate,
                request.PainZones,
                request.HasActiveInjury),
            cancellationToken);

        return result.Created
            ? Created($"/api/v1/athletes/{athleteId}/check-in", result.Response)
            : Ok(result.Response);
    }
}