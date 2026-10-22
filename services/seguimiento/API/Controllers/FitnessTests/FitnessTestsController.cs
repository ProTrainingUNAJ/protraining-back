using Application.DTOs.FitnessTests;
using Application.UseCases.FitnessTests.ListFitnessTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.FitnessTests;

[ApiController]
[Route("api/v1/fitness-tests")]
[Authorize]
public sealed class FitnessTestsController(ListFitnessTestsQueryHandler handler) : ControllerBase
{
    // Catálogo de tests por capacidad motora (KAN-333): el filtro es opcional y un filtro
    // sin resultados responde 200 con lista vacía, nunca 404.
    [HttpGet]
    [ProducesResponseType(typeof(List<FitnessTestDto>), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<ActionResult<List<FitnessTestDto>>> List(
        [FromQuery] ListFitnessTestsQuery query,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(query, cancellationToken);
        return Ok(result);
    }
}
