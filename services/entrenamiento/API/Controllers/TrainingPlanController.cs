using Application.DTOs.TrainingPlans;
using Application.UseCases.TrainingPlans.AssignPlan;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/v1/training-plans")]
[ApiController]
[Authorize]
public class TrainingPlanController : ControllerBase
{
    private readonly AssignPlanCommandHandler _assignPlanHandler;

    public TrainingPlanController(AssignPlanCommandHandler assignPlanHandler)
    {
        _assignPlanHandler = assignPlanHandler;
    }

    [HttpPost("assignments")]
    public async Task<IActionResult> AssignPlan([FromBody] AssignPlanRequest request, CancellationToken cancellationToken = default)
    {
        var command = new AssignPlanCommand(
            request.TrainingPlanId,
            request.StartDate,
            request.CategoryId,
            request.AthleteId
        );

        var assignmentId = await _assignPlanHandler.Handle(command, cancellationToken);
        return Created(string.Empty, new { assignmentId, message = "Plan asignado exitosamente." });
    }
}
