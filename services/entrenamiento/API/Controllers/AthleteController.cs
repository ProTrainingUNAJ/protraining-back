using Application.DTOs.AthleteRecords;
using Application.DTOs.Routines;
using Application.Exceptions;
using Application.Interfaces.Services;
using Application.UseCases.AthleteRecord.ListAthleteRecords;
using Application.UseCases.AthleteRecord.RegisterAthleteRecord;
using Application.UseCases.Routines.GetTodayRoutine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/v1/athletes")]
    [ApiController]
    [Authorize]
    public class AthleteController : ControllerBase
    {
        private readonly GetTodayRoutineQueryHandler queryHandler;
        private readonly ListAthleteRecordsQueryHandler listRecordsHandler;
        private readonly RegisterAthleteRecordCommandHandler registerRecordHandler;
        private readonly IStructureClient structureClient;

        public AthleteController(
            GetTodayRoutineQueryHandler queryHandler,
            ListAthleteRecordsQueryHandler listRecordsHandler,
            RegisterAthleteRecordCommandHandler registerRecordHandler,
            IStructureClient structureClient)
        {
            this.queryHandler = queryHandler;
            this.listRecordsHandler = listRecordsHandler;
            this.registerRecordHandler = registerRecordHandler;
            this.structureClient = structureClient;
        }

        [HttpGet("{athleteId:guid}/today-routine")]
        [Authorize(Roles = "PreparadorFisico")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<TodayRoutineResponseDto>> GetTodayRoutine([FromRoute] Guid athleteId, [FromQuery] DateOnly? date = null, CancellationToken cancellationToken = default)
        {
            var query = new GetTodayRoutineQuery { AthleteId = athleteId, Date = date };

            var result = await queryHandler.Handle(query, cancellationToken);

            return Ok(result);
        }

        [HttpGet("{athleteId:guid}/records")]
        [Authorize(Roles = "PreparadorFisico,Atleta")]
        [Tags("Records")]
        public async Task<ActionResult<List<AthleteRecordDto>>> GetRecords(
            [FromRoute] Guid athleteId,
            [FromQuery] bool? currentOnly = null,
            [FromQuery] Guid? exerciseId = null,
            [FromQuery] DateOnly? from = null,
            [FromQuery] DateOnly? to = null,
            CancellationToken cancellationToken = default)
        {
            if (User.IsInRole("PreparadorFisico"))
            {
                var athlete = await structureClient.GetAthleteByIdAsync(athleteId, cancellationToken);
                if (athlete == null)
                    throw new NotFoundException($"No se encontró el atleta con ID '{athleteId}'.");
            }
            else
            {
                var me = await structureClient.GetMeAsync(cancellationToken);
                if (me is null)
                    throw new NotFoundException("No se encontró un perfil de atleta vinculado a la cuenta actual.");
                if (me.AthleteId != athleteId)
                    throw new NotFoundException($"No se encontró el atleta con ID '{athleteId}'.");
            }

            var result = await listRecordsHandler.Handle(
                new ListAthleteRecordsQuery(athleteId, currentOnly, exerciseId, from, to),
                cancellationToken);
            return Ok(result);
        }

        [HttpPost("{athleteId:guid}/records")]
        [Authorize(Roles = "PreparadorFisico")]
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
