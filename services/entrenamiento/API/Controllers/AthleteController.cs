using Application.DTOs.AthleteRecords;
using Application.DTOs.Routines;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Application.UseCases.AthleteRecord.RegisterAthleteRecord;
using Application.UseCases.Routines.GetTodayRoutine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/v1/athletes")]
    [ApiController]
    [Authorize(Roles = "PreparadorFisico")]
    public class AthleteController : ControllerBase
    {
        private readonly GetTodayRoutineQueryHandler queryHandler;
        private readonly ListAthleteRecordsQueryHandler listRecordsHandler;
        private readonly RegisterAthleteRecordCommandHandler registerRecordHandler;

        public AthleteController(
            GetTodayRoutineQueryHandler queryHandler,
            ListAthleteRecordsQueryHandler listRecordsHandler,
            RegisterAthleteRecordCommandHandler registerRecordHandler)
        {
            this.queryHandler = queryHandler;
            this.listRecordsHandler = listRecordsHandler;
            this.registerRecordHandler = registerRecordHandler;
        }

        [HttpGet("{athleteId:guid}/today-routine")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<TodayRoutineResponseDto>> GetTodayRoutine([FromRoute] Guid athleteId, [FromQuery] DateOnly? date = null, CancellationToken cancellationToken = default)
        {
            var query = new GetTodayRoutineQuery { AthleteId = athleteId, Date = date };

            var result = await queryHandler.Handle(query, cancellationToken);

            return Ok(result);
        }

        [HttpGet("{athleteId:guid}/records")]
        [Tags("Records")]
        public async Task<ActionResult<List<AthleteRecordDto>>> GetRecords(
            [FromRoute] Guid athleteId,
            [FromQuery] bool currentOnly = true,
            CancellationToken cancellationToken = default)
        {
            var result = await listRecordsHandler.Handle(new ListAthleteRecordsQuery(athleteId, currentOnly), cancellationToken);
            return Ok(result);
        }

        [HttpPost("{athleteId:guid}/records")]
        [Tags("Records")]
        public async Task<ActionResult<AthleteRecordDto>> SaveRecord(
            [FromRoute] Guid athleteId,
            [FromBody] SaveAthleteRecordRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await registerRecordHandler.Handle(new RegisterAthleteRecordCommand(athleteId, request), cancellationToken);
            return Ok(result);
        }
    }
}
