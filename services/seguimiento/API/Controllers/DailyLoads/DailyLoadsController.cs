using Application.DTOs.DailyLoad;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.DailyLoads.GetAthleteWeek;
using Application.UseCases.DailyLoads.RegisterDailyLoad;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.DailyLoads;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/daily-load")]
[Authorize]
public sealed class DailyLoadsController(
    RegisterDailyLoadCommandHandler handler,
    GetAthleteWeekQueryHandler weekHandler,
    IStructureClient structureClient) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<DailyLoadResponseDto>> Register(
        Guid athleteId, RegisterDailyLoadRequest request, CancellationToken cancellationToken)
    {
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null) throw new NoAthleteLinkedException();
        if (me.AthleteId != athleteId) throw new ForbiddenException();

        var result = await handler.HandleAsync(
            new RegisterDailyLoadCommand(athleteId, request), cancellationToken);

        return result.Created
            ? Created($"/api/v1/athletes/{athleteId}/daily-load", result.Response)
            : Ok(result.Response);
    }

    [HttpGet]
    public async Task<ActionResult<DailyLoadResponseDto>> Get(
        Guid athleteId, CancellationToken cancellationToken)
    {
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null) throw new NoAthleteLinkedException();
        if (me.AthleteId != athleteId) throw new ForbiddenException();

        return Ok(await handler.GetTodayAsync(athleteId, cancellationToken));
    }

    [HttpGet("week")]
    public async Task<ActionResult<AthleteWeekDto>> GetWeek(
        Guid athleteId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var me = await structureClient.GetMeAsync(cancellationToken);
        if (me is null) throw new NoAthleteLinkedException();
        if (me.AthleteId != athleteId) throw new ForbiddenException();

        return Ok(await weekHandler.HandleAsync(
            new GetAthleteWeekQuery(athleteId, date), cancellationToken));
    }
}
