using Application.DTOs.CheckIns;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.CheckIns.ListAthleteCheckIns;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.CheckIns;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/check-in")]
[Authorize]
public sealed class CheckInsController(
    SubmitCheckInCommandHandler handler,
    ListAthleteCheckInsQueryHandler listHandler,
    IStructureClient structureClient) : ControllerBase
{
    [HttpGet("~/api/v1/athletes/{athleteId:guid}/check-ins")]
    [ProducesResponseType(typeof(IReadOnlyList<CheckInDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CheckInDto>>> List(
        Guid athleteId,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (User.IsInRole("PreparadorFisico"))
        {
            if (!await structureClient.IsOwnAthleteAsync(athleteId, cancellationToken))
                throw new AthleteNotFoundException();
        }
        else
        {
            var me = await structureClient.GetMeAsync(cancellationToken);
            if (me is null)
                throw new NoAthleteLinkedException();
            if (me.AthleteId != athleteId)
                throw new AthleteNotFoundException();
        }

        var result = await listHandler.HandleAsync(
            new ListAthleteCheckInsQuery(athleteId, from, to, limit),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CheckInResponseDto), 200)]
    [ProducesResponseType(typeof(CheckInResponseDto), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<CheckInResponseDto>> Submit(
        Guid athleteId,
        SubmitCheckInRequest request,
        CancellationToken cancellationToken)
    {
        // Seguridad (KAN-329): quién es el atleta sale del token, no de la URL.
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null)
            throw new NoAthleteLinkedException();
        // Id de otro atleta → 404, no 403: no revela si ese id existe (KAN-431).
        if (me.AthleteId != athleteId)
            throw new AthleteNotFoundException();

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
                request.HasActiveInjury,
                request.Date),
            cancellationToken);

        return result.Created
            ? Created($"/api/v1/athletes/{athleteId}/check-in", result.Response)
            : Ok(result.Response);
    }
}