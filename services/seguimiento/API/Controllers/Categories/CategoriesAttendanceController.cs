using Application.DTOs.Attendance;
using Application.UseCases.Attendance.GetCategoryAttendanceToday;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Categories;

// Asistencia automática del día por categoría para el PF (KAN-445): se deduce a partir de las
// cargas del día cargadas por los propios atletas, sin tomar lista.
[ApiController]
[Route("api/v1/categories")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class CategoriesAttendanceController(
    GetCategoryAttendanceTodayQueryHandler handler) : ControllerBase
{
    [HttpGet("{categoryId:guid}/attendance-today")]
    [ProducesResponseType(typeof(CategoryAttendanceSummaryDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<CategoryAttendanceSummaryDto>> GetByCategoryToday(
        Guid categoryId,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new GetCategoryAttendanceTodayQuery(categoryId),
            cancellationToken);

        return Ok(result);
    }
}