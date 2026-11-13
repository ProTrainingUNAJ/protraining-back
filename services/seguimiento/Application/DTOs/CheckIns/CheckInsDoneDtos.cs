namespace Application.DTOs.CheckIns;

public record CheckInsDoneRequest(DateOnly Date, Guid PfId, IReadOnlyList<Guid> AthleteIds);

public record CheckInsDoneResponse(IReadOnlyList<Guid> AthleteIds);