using Application.DTOs.Injuries;
using Application.UseCases.Injuries.GetActiveInjuriesSummary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Injuries;

// Consulta batch de lesiones activas por lote de atletas (KAN-464): evita el N+1 desde
// el modal de asignaciones de planificación consultando en una sola query agrupada.
[ApiController]
[Route("api/v1/injuries/active-summary")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class ActiveInjuriesSummaryController(GetActiveInjuriesSummaryQueryHandler handler) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AthleteActiveInjurySummaryDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<IReadOnlyList<AthleteActiveInjurySummaryDto>>> GetSummary(
        [FromQuery] Guid[]? athleteIds,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new GetActiveInjuriesSummaryQuery(athleteIds ?? []),
            cancellationToken);

        return Ok(result);
    }
}
