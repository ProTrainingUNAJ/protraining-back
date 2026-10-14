using Application.DTOs.Exercise;
using Application.UseCases.Exercise.ListExercises;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers
{
    [Route("api/v1/exercises")]
    [ApiController]
    [Authorize]
    public class ExerciseController : ControllerBase
    {
        private readonly ListExercisesQueryHandler queryHandler;

        public ExerciseController(ListExercisesQueryHandler queryHandler)
        {
            this.queryHandler = queryHandler;
        }

        [HttpGet]
        public async Task<ActionResult<List<ExerciseDto>>> List([FromQuery] ListExercisesQuery query, CancellationToken cancellationToken = default)
        {
            var result = await queryHandler.Handle(query, cancellationToken);
            return Ok(result);
        }

    }
}
