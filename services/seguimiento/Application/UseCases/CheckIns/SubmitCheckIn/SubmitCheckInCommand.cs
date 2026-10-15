using Application.DTOs.CheckIns;

namespace Application.UseCases.CheckIns.SubmitCheckIn;

// Envío (o actualización) del check-in diario del atleta.
public sealed record SubmitCheckInCommand(Guid AthleteId, CheckInRequestDto Request);
