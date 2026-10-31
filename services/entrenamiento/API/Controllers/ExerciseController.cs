using Application.DTOs.Exercise;
using Application.UseCases.Exercise.AddExercise;
using Application.UseCases.Exercise.CheckRestrictions;
using Application.UseCases.Exercise.GetSubstitutes;
using Application.UseCases.Exercise.ListExercises;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
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
        private readonly CreateExerciseCommandHandler _createExerciseCommandHandler;

        public ExerciseController(
            ListExercisesQueryHandler listExercisesQueryHandler,
            CheckExerciseRestrictionsQueryHandler checkRestrictionsQueryHandler,
            GetExerciseSubstitutesQueryHandler getSubstitutesQueryHandler,
            CreateExerciseCommandHandler createExerciseCommandHandler)
        {
            _listExercisesQueryHandler = listExercisesQueryHandler;
            _checkRestrictionsQueryHandler = checkRestrictionsQueryHandler;
            _getSubstitutesQueryHandler = getSubstitutesQueryHandler;
            _createExerciseCommandHandler = createExerciseCommandHandler;
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

        [HttpPost]
        [Authorize(Roles = "PreparadorFisico")]
        public async Task<ActionResult<ExerciseDto>> CreateExercise([FromBody] CreateExerciseCommand request, CancellationToken cancellationToken = default)
        {
            var result = await _createExerciseCommandHandler.Handle(request, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, result);
        }
    }
}
