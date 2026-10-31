using Application.DTOs.AthleteRecords;
using Application.DTOs.RecordProposals;
using Application.UseCases.RecordProposals.ApproveRecordProposal;
using Application.UseCases.RecordProposals.DiscardRecordProposal;
using Application.UseCases.RecordProposals.ListRecordProposals;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/record-proposals")]
[ApiController]
[Authorize(Roles = "PreparadorFisico")]
public class RecordProposalsController : ControllerBase
{
    private readonly ListRecordProposalsQueryHandler _listHandler;
    private readonly ApproveRecordProposalCommandHandler _approveHandler;
    private readonly DiscardRecordProposalCommandHandler _discardHandler;

    public RecordProposalsController(
        ListRecordProposalsQueryHandler listHandler,
        ApproveRecordProposalCommandHandler approveHandler,
        DiscardRecordProposalCommandHandler discardHandler)
    {
        _listHandler = listHandler;
        _approveHandler = approveHandler;
        _discardHandler = discardHandler;
    }

    [HttpGet]
    [Tags("Records")]
    public async Task<ActionResult<List<RecordProposalDto>>> List(
        [FromQuery] RecordProposalStatus? status = RecordProposalStatus.Pending,
        CancellationToken cancellationToken = default)
    {
        var result = await _listHandler.Handle(new ListRecordProposalsQuery(status), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    [Tags("Records")]
    public async Task<ActionResult<AthleteRecordDto>> Approve(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await _approveHandler.Handle(new ApproveRecordProposalCommand(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/discard")]
    [Tags("Records")]
    public async Task<IActionResult> Discard(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        await _discardHandler.Handle(new DiscardRecordProposalCommand(id), cancellationToken);
        return NoContent();
    }
}

