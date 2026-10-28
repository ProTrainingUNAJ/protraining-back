using Application.DTOs.Routines;
using Application.UseCases.Routines.GetTodayRoutine;
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
        public MeController(GetTodayRoutineQueryHandler queryHandler)
        {
            this.queryHandler = queryHandler;
        }

        [HttpGet("today-routine")]
        [Tags("Execution and Loads")]
        public async Task<ActionResult<TodayRoutineResponseDto>> GetMyTodayRoutine([FromQuery] DateOnly? date = null, CancellationToken cancellationToken = default)
        {
            var result = await queryHandler.Handle(new GetTodayRoutineQuery { Date = date }, cancellationToken);

            return Ok(result);
        }

    }
}
