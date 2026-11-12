using Application.DTOs.Athletes;
using Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

/// <summary>
/// Atletas de una categoría para otros microservicios con rol 'Servicio' (KAN-578).
/// Lo usa Seguimiento para la comparación del atleta con su grupo: el atleta no puede listar
/// los atletas de su categoría con su propio token (ese endpoint es solo del PF).
/// </summary>
[ApiController]
[Route("api/v1/internal/categories/{categoryId:guid}/athletes")]
[Authorize(Roles = "Servicio")]
public sealed class InternalCategoryAthletesController(
    IAthleteCategoryRepository athleteCategoryRepository) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InternalCategoryAthleteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<InternalCategoryAthleteDto>>> GetAthletes(
        [FromRoute] Guid categoryId,
        CancellationToken cancellationToken)
    {
        var athletes = await athleteCategoryRepository.ListAthletesByCategoryInternalAsync(categoryId, cancellationToken);
        return Ok(athletes.Select(a => new InternalCategoryAthleteDto(a.Id, a.FullName)).ToList());
    }
}
