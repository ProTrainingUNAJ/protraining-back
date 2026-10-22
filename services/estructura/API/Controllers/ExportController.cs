using Application.UseCases.Export.ExportStructureCsv;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[ApiController]
[Route("api/v1/export")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class ExportController(ExportStructureCsvQueryHandler exportHandler) : ControllerBase
{
    [HttpGet("csv")]
    public async Task<IActionResult> ExportStructureCsv(CancellationToken cancellationToken)
    {
        var result = await exportHandler.HandleAsync(new ExportStructureCsvQuery(), cancellationToken);
        return File(result.FileBytes, result.ContentType, result.FileName);
    }
}
