using Application.DTOs.Exercise;
using Application.UseCases.Exercise.CheckRestrictions;
using Application.UseCases.Exercise.GetSubstitutes;
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
        private readonly ListExercisesQueryHandler _listExercisesQueryHandler;
        private readonly CheckExerciseRestrictionsQueryHandler _checkRestrictionsQueryHandler;
        private readonly GetExerciseSubstitutesQueryHandler _getSubstitutesQueryHandler;

        public ExerciseController(
            ListExercisesQueryHandler listExercisesQueryHandler,
            CheckExerciseRestrictionsQueryHandler checkRestrictionsQueryHandler,
            GetExerciseSubstitutesQueryHandler getSubstitutesQueryHandler)
        {
            _listExercisesQueryHandler = listExercisesQueryHandler;
            _checkRestrictionsQueryHandler = checkRestrictionsQueryHandler;
            _getSubstitutesQueryHandler = getSubstitutesQueryHandler;
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedExerciseResponse>> List([FromQuery] ListExercisesQuery query, CancellationToken cancellationToken = default)
        {
            var result = await _listExercisesQueryHandler.Handle(query, cancellationToken);
            return Ok(result);
        }

        [HttpPost("restrictions/check")]
        public async Task<ActionResult<List<ExerciseRestrictionCheckResultDto>>> CheckRestrictions(
            [FromBody] CheckExerciseRestrictionsRequest request,
            CancellationToken cancellationToken = default)
        {
            var query = new CheckExerciseRestrictionsQuery
            {
                PainZones = request?.PainZones,
                ExerciseIds = request?.ExerciseIds ?? []
            };
            var result = await _checkRestrictionsQueryHandler.Handle(query, cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/substitutes")]
        public async Task<ActionResult<List<ExerciseSubstituteDto>>> GetSubstitutes(
            [FromRoute] Guid id,
            [FromQuery] string zone,
            CancellationToken cancellationToken = default)
        {
            var query = new GetExerciseSubstitutesQuery
            {
                ExerciseId = id,
                Zone = zone
            };
            var result = await _getSubstitutesQueryHandler.Handle(query, cancellationToken);
            return Ok(result);
        }
    }
}
