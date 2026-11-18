namespace Application.Interfaces.Services;

public sealed record ServicePurgeResult(
    string ServiceName,
    bool IsSuccess,
    int? StatusCode = null,
    string? ErrorMessage = null);

