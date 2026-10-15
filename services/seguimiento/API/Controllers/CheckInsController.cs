using Application.DTOs.CheckIns;
using Application.UseCases.CheckIns.SubmitCheckIn;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

// Check-in diario del atleta. El semáforo se calcula en el motor local del Dominio con los datos
// de esta base: ninguna llamada a otros servicios en el flujo (KAN-364).
[ApiController]
[Route("api/v1/athletes/{athleteId:guid}/check-in")]
public sealed class CheckInsController(SubmitCheckInCommandHandler handler) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CheckInResponseDto), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> Post(
        Guid athleteId,
        [FromBody] CheckInRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var response = await handler.HandleAsync(new SubmitCheckInCommand(athleteId, request), cancellationToken);
        return Ok(response);
    }
}
