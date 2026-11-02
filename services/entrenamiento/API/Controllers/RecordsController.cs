using Application.DTOs.AthleteRecords;
using Application.UseCases.AthleteRecord.GetRecordsReliability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/records")]
[ApiController]
[Authorize(Roles = "PreparadorFisico")]
public class RecordsController : ControllerBase
{
    private readonly GetRecordsReliabilityQueryHandler _reliabilityHandler;

    public RecordsController(GetRecordsReliabilityQueryHandler reliabilityHandler)
    {
        _reliabilityHandler = reliabilityHandler;
    }

    [HttpGet("reliability")]
    [Tags("Records")]
    public async Task<ActionResult<RecordReliabilitySummaryDto>> GetReliability(
        [FromQuery] Guid categoryId,
        [FromQuery] Guid? exerciseId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _reliabilityHandler.Handle(
            new GetRecordsReliabilityQuery(categoryId, exerciseId),
            cancellationToken);
        return Ok(result);
    }
}