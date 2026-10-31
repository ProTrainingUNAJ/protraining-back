using Application.DTOs.AthleteRecords;
using Application.UseCases.AthleteRecord.ConfirmRecordsImport;
using Application.UseCases.AthleteRecord.PreviewRecordsImport;
using Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/records/import")]
[ApiController]
[Authorize(Roles = "PreparadorFisico")]
public class RecordsImportController : ControllerBase
{
    private readonly PreviewRecordsImportQueryHandler _previewHandler;
    private readonly ConfirmRecordsImportCommandHandler _confirmHandler;

    public RecordsImportController(
        PreviewRecordsImportQueryHandler previewHandler,
        ConfirmRecordsImportCommandHandler confirmHandler)
    {
        _previewHandler = previewHandler;
        _confirmHandler = confirmHandler;
    }

    [HttpPost("preview")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<List<ImportRecordPreviewRowDto>>> Preview(
        IFormFile file,
        [FromForm] string athleteColumn,
        [FromForm] string exerciseColumn,
        [FromForm] string weightColumn,
        [FromForm] string? dateColumn,
        [FromForm] string? sourceColumn,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            throw new DomainException("El archivo CSV es obligatorio.");
        if (file.Length > 1_048_576)
            throw new DomainException("El archivo supera el tamaño máximo de 1 MB.");

        using var reader = new StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8);
        var content = await reader.ReadToEndAsync(cancellationToken);

        var result = await _previewHandler.Handle(
            new PreviewRecordsImportQuery(content, athleteColumn, exerciseColumn, weightColumn, dateColumn, sourceColumn),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("confirm")]
    public async Task<ActionResult<List<AthleteRecordDto>>> Confirm(
        [FromBody] ConfirmImportRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _confirmHandler.Handle(new ConfirmRecordsImportCommand(request), cancellationToken);
        return Ok(result);
    }
}
