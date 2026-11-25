using Application.DTOs.FitnessTests;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.FitnessTests.ListAthleteBestMarks;
using Application.UseCases.FitnessTests.ListAthleteFitnessTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.FitnessTests;

[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/fitness-tests")]
[Authorize]
public sealed class AthleteFitnessTestsController(
    ListAthleteFitnessTestsQueryHandler listHandler,
    ListAthleteBestMarksQueryHandler listBestMarksHandler,
    IAthleteAccessGuard athleteAccessGuard) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteFitnessTestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AthleteFitnessTestDto>>> List(
        Guid athleteId,
        [FromQuery] Guid? testId = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(athleteId, cancellationToken);

        var result = await listHandler.HandleAsync(
            new ListAthleteFitnessTestsQuery(athleteId, testId, from, to),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("best-marks")]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteBestMarkDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AthleteBestMarkDto>>> ListBestMarks(
    Guid athleteId,
    CancellationToken cancellationToken = default)
    {
        await athleteAccessGuard.EnsureCanReadAsync(athleteId, cancellationToken);
        var result = await listBestMarksHandler.HandleAsync(new ListAthleteBestMarksQuery(athleteId), cancellationToken);
        return Ok(result);
    }
}

