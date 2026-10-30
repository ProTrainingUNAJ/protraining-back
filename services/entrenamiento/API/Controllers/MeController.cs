using Application.DTOs.Routines;
using Application.UseCases.Routines.GetTodayRoutine;
using Application.UseCases.Routines.RecordPerformedSets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/v1/me")]
    [ApiController]
    [Authorize(Roles = "Atleta")]
    public class MeController : ControllerBase
    {
        private readonly GetTodayRoutineQueryHandler queryHandler;
        private readonly RecordPerformedSetsCommandHandler recordPerformedSetsHandler;

        public MeController(
            GetTodayRoutineQueryHandler queryHandler,
            RecordPerformedSetsCommandHandler recordPerformedSetsHandler)
        {
            this.queryHandler = queryHandler;
            this.recordPerformedSetsHandler = recordPerformedSetsHandler;
        }

        [HttpGet("today-routine")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<TodayRoutineResponseDto>> GetMyTodayRoutine([FromQuery] DateOnly? date = null, CancellationToken cancellationToken = default)
        {
            var result = await queryHandler.Handle(new GetTodayRoutineQuery { Date = date }, cancellationToken);

            return Ok(result);
        }

        [HttpPost("today-routine/performed-sets")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<RecordPerformedSetsResponseDto>> RecordPerformedSets(
            [FromBody] List<PerformedSetInputDto> sets,
            CancellationToken cancellationToken = default)
        {
            var result = await recordPerformedSetsHandler.Handle(new RecordPerformedSetsCommand(sets), cancellationToken);

            return Ok(result);
        }
    }
}
