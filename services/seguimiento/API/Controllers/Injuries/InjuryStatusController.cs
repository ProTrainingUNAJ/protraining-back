using Application.DTOs.Injuries;
using Application.UseCases.Injuries.UpdateInjuryStatus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers.Injuries;

// Máquina de estados de las lesiones (KAN-420): el rol PreparadorFisico se valida en este
// servicio, como el resto de los datos del PF en Seguimiento. Las reglas (transición válida,
// nota clínica obligatoria, alta con liberación del atleta) las aplica el dominio y salen
// como ProblemDetails: 400 si la transición está prohibida, 404 si la lesión no existe.
[ApiController]
[Route("api/v1/injuries/{injuryId:guid}/status")]
[Authorize(Roles = "PreparadorFisico")]
public sealed class InjuryStatusController(UpdateInjuryStatusCommandHandler handler) : ControllerBase
{
    // Transición de estado (contrato: updateInjuryStatus): 200 con la lesión actualizada.
    // Active → MedicallyCleared directo responde 400 avisando que debe pasar por "En recuperación".
    [HttpPatch]
    [ProducesResponseType(typeof(AthleteInjuryDto), 200)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    [ProducesResponseType(typeof(ProblemDetails), 401)]
    [ProducesResponseType(typeof(ProblemDetails), 403)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<ActionResult<AthleteInjuryDto>> UpdateStatus(
        Guid injuryId,
        UpdateInjuryStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await handler.HandleAsync(
            new UpdateInjuryStatusCommand(injuryId, request.NewStatus!.Value, request.TransitionNotes),
            cancellationToken);

        return Ok(result);
    }
}
