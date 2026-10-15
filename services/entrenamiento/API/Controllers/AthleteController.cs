using Application.DTOs.Routines;
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

        public AthleteController(GetTodayRoutineQueryHandler queryHandler)
        {
            this.queryHandler = queryHandler;
        }

        [HttpGet("{athleteId:guid}/today-routine")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<TodayRoutineResponseDto>> GetTodayRoutine([FromRoute] Guid athleteId, CancellationToken cancellationToken = default)
        {
            var query = new GetTodayRoutineQuery{ AthleteId = athleteId };

            var result = await queryHandler.Handle(query, cancellationToken);

            return Ok(result);
        }

    }
}
