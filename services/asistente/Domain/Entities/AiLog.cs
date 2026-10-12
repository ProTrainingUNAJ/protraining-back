namespace Domain.Entities;

/// <summary>
/// Represents an audit and telemetry log of an AI model or rule-based engine invocation.
/// Stores timestamps, provider identity, sanitized prompts, responses, latencies,
/// token usage metrics, and validation outcomes.
/// </summary>
public sealed class AiLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public Guid? PfId { get; private set; }
    public Guid? AthleteId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Prompt { get; private set; } = string.Empty;
    public string? Response { get; private set; }
    public long LatencyMs { get; private set; }
    public int? PromptTokens { get; private set; }
    public int? CompletionTokens { get; private set; }
    public int? TotalTokens { get; private set; }
    public bool IsValid { get; private set; }
    public string ValidationResult { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }

    private AiLog() { }

    public static AiLog Create(
        string provider,
        string prompt,
        long latencyMs,
        bool isValid,
        string validationResult,
        string? response = null,
        Guid? pfId = null,
        Guid? athleteId = null,
        int? promptTokens = null,
        int? completionTokens = null,
        int? totalTokens = null,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("El proveedor no puede estar vacío.", nameof(provider));
        if (string.IsNullOrWhiteSpace(prompt))
            throw new ArgumentException("El prompt no puede estar vacío.", nameof(prompt));

        return new AiLog
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            PfId = pfId,
            AthleteId = athleteId,
            Provider = provider.Trim(),
            Prompt = prompt.Trim(),
            Response = response,
            LatencyMs = latencyMs,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens ?? ((promptTokens.HasValue || completionTokens.HasValue) ? (promptTokens ?? 0) + (completionTokens ?? 0) : null),
            IsValid = isValid,
            ValidationResult = validationResult.Trim(),
            ErrorMessage = errorMessage
        };
    }

    public void AssignPfId(Guid pfId)
    {
        if (!PfId.HasValue && pfId != Guid.Empty)
        {
            PfId = pfId;
        }
    }
}
