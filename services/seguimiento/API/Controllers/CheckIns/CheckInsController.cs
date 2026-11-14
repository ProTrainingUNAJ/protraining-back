using Application.DTOs.CheckIns;
using Application.DTOs.Injuries;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.CheckIns.GetTodayCheckIn;
using Application.UseCases.CheckIns.ListAthleteCheckIns;
using Application.UseCases.CheckIns.ListCheckInInjuries;
using Application.UseCases.CheckIns.ListResolvedInjuries;
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
    GetTodayCheckInQueryHandler getTodayHandler,
    ListCheckInInjuriesQueryHandler listInjuriesHandler,
    ListResolvedInjuriesQueryHandler listResolvedInjuriesHandler,
    IStructureClient structureClient,
    IAthleteAccessGuard athleteAccessGuard) : ControllerBase
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
        await athleteAccessGuard.EnsureCanReadAsync(athleteId, cancellationToken);

        var result = await listHandler.HandleAsync(
            new ListAthleteCheckInsQuery(athleteId, from, to, limit),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("today")]
    [Authorize(Roles = "PreparadorFisico")]
    [ProducesResponseType(typeof(CheckInDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<CheckInDto>> GetToday(
        Guid athleteId,
        CancellationToken cancellationToken)
    {
        var result = await getTodayHandler.HandleAsync(new GetTodayCheckInQuery(athleteId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("injuries")]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteInjuryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AthleteInjuryDto>>> ListInjuries(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(athleteId, cancellationToken);

        var result = await listInjuriesHandler.HandleAsync(
            new ListCheckInInjuriesQuery(athleteId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("injuries/resolved")]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteInjuryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AthleteInjuryDto>>> ListResolvedInjuries(
        Guid athleteId,
        CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(athleteId, cancellationToken);

        var result = await listResolvedInjuriesHandler.HandleAsync(
            new ListResolvedInjuriesQuery(athleteId),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(CheckInResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CheckInResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CheckInResponseDto>> Submit(
        Guid athleteId,
        SubmitCheckInRequest request,
        CancellationToken cancellationToken)
    {
        // Seguridad: quién es el atleta sale del token, no de la URL.
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null)
            throw new NoAthleteLinkedException();
        // Id de otro atleta → 404, no 403: no revela si ese id existe.
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
                request.Date,
                request.Injuries?.Select(i => new SubmitInjuryPain(i.InjuryId, i.Level)).ToList()),
            cancellationToken);

        return result.Created
            ? Created($"/api/v1/athletes/{athleteId}/check-in", result.Response)
            : Ok(result.Response);
    }
}
