using Domain.Common;
using Domain.Exceptions;

namespace Domain.Entities;

public enum RecommendationType
{
    Training,
    Habits
}

public enum RecommendationStatus
{
    Pending,
    Approved,
    Discarded
}

/// <summary>
/// Recomendación generada por el motor determinístico + IA para revisión del preparador físico (#196 · KAN-491).
/// Nace en estado Pending y permanece oculta al atleta hasta la aprobación del PF (KAN-492).
/// </summary>
public sealed class AthleteRecommendation : BaseEntity
{
    public const int MaxCategoryNameLength = 150;
    public const int MaxTitleLength = 200;
    public const int MaxReasonLength = 300;
    public const int MaxRecommendationTextLength = 2000;
    public const int MaxProviderLength = 80;

    public Guid AthleteId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public RecommendationType Type { get; private set; } = RecommendationType.Training;
    public RecommendationStatus Status { get; private set; } = RecommendationStatus.Pending;
    public string Title { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public string RecommendationText { get; private set; } = string.Empty;
    public string MetricsSnapshotJson { get; private set; } = "{}";
    public string Provider { get; private set; } = "Reglas";
    public DateTime? ReviewedAtUtc { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private AthleteRecommendation() { }

    public static AthleteRecommendation Create(
        Guid athleteId,
        string title,
        string reason,
        string recommendationText,
        Guid? categoryId = null,
        string? categoryName = null,
        RecommendationType type = RecommendationType.Training,
        string? metricsSnapshotJson = null,
        string? provider = null)
    {
        if (athleteId == Guid.Empty)
            throw new DomainException("El identificador del atleta es obligatorio.");

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("El título de la recomendación es obligatorio.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("El motivo de la recomendación es obligatorio.");

        if (string.IsNullOrWhiteSpace(recommendationText))
            throw new DomainException("El texto de la recomendación es obligatorio.");

        var normalizedCategoryId = categoryId.HasValue && categoryId.Value != Guid.Empty
            ? categoryId.Value
            : (Guid?)null;

        return new AthleteRecommendation
        {
            AthleteId = athleteId,
            CategoryId = normalizedCategoryId,
            CategoryName = TruncateOptional(categoryName, MaxCategoryNameLength),
            Type = type,
            Status = RecommendationStatus.Pending,
            Title = TruncateRequired(title, MaxTitleLength),
            Reason = TruncateRequired(reason, MaxReasonLength),
            RecommendationText = TruncateRequired(recommendationText, MaxRecommendationTextLength),
            MetricsSnapshotJson = string.IsNullOrWhiteSpace(metricsSnapshotJson) ? "{}" : metricsSnapshotJson.Trim(),
            Provider = string.IsNullOrWhiteSpace(provider) ? "Reglas" : TruncateRequired(provider, MaxProviderLength),
            UpdatedAt = DateTime.UtcNow
        };
    }

    public void UpdateContent(string title, string recommendationText)
    {
        EnsurePending();

        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("El título de la recomendación es obligatorio.");

        if (string.IsNullOrWhiteSpace(recommendationText))
            throw new DomainException("El texto de la recomendación es obligatorio.");

        Title = TruncateRequired(title, MaxTitleLength);
        RecommendationText = TruncateRequired(recommendationText, MaxRecommendationTextLength);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Approve(DateTime? utcNow = null)
    {
        EnsurePending();
        var now = utcNow ?? DateTime.UtcNow;
        Status = RecommendationStatus.Approved;
        ReviewedAtUtc = now;
        UpdatedAt = now;
    }

    public void Discard(DateTime? utcNow = null)
    {
        EnsurePending();
        var now = utcNow ?? DateTime.UtcNow;
        Status = RecommendationStatus.Discarded;
        ReviewedAtUtc = now;
        UpdatedAt = now;
    }

    private void EnsurePending()
    {
        if (Status != RecommendationStatus.Pending)
            throw new DomainException(GetNonPendingStatusMessage());
    }

    public string GetNonPendingStatusMessage() => Status switch
    {
        RecommendationStatus.Approved => "La recomendación ya fue aprobada.",
        RecommendationStatus.Discarded => "La recomendación ya fue descartada.",
        _ => "La recomendación se encuentra pendiente de revisión."
    };

    private static string TruncateRequired(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string? TruncateOptional(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}
