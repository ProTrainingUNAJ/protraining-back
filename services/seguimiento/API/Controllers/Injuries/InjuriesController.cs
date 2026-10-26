using Application.DTOs.Injuries;
using Application.UseCases.Injuries.CreateAthleteInjury;
using Application.UseCases.Injuries.ListAthleteInjuries;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Injuries;

// Lesiones de un atleta con su ciclo de vida (KAN-419): el rol PreparadorFisico se valida en
// este servicio, como el resto de los datos del PF en Seguimiento. Las reglas de negocio
// (estado inicial Active, fechas) las aplica el dominio y salen como ProblemDetails.
[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/injuries")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class InjuriesController(
    CreateAthleteInjuryCommandHandler createHandler,
    ListAthleteInjuriesQueryHandler listHandler) : ControllerBase
{
    // Historial de lesiones del atleta (contrato: listAthleteInjuries). El filtro por estado
    // es opcional y un filtro sin resultados responde 200 con lista vacía, nunca 404.
    [HttpGet]
    [ProducesResponseType(typeof(List<AthleteInjuryDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<List<AthleteInjuryDto>>> List(
        Guid athleteId,
        [FromQuery] InjuryStatus? status,
        CancellationToken cancellationToken = default)
    {
        var result = await listHandler.HandleAsync(
            new ListAthleteInjuriesQuery(athleteId, status),
            cancellationToken);

        return Ok(result);
    }

    // Da de alta una lesión (contrato: createInjury): siempre nace en Active, con fecha de
    // retorno estimada obligatoria y posterior a la de la lesión. Violaciones de regla → 400.
    [HttpPost]
    [ProducesResponseType(typeof(AthleteInjuryDto), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    public async Task<ActionResult<AthleteInjuryDto>> Create(
        Guid athleteId,
        CreateAthleteInjuryRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await createHandler.HandleAsync(
            new CreateAthleteInjuryCommand(
                athleteId,
                request.Zone,
                request.Description,
                request.InjuryDate,
                request.EstimatedReturnDate),
            cancellationToken);

        return Created($"/api/v1/athletes/{athleteId}/injuries", result);
    }
}
