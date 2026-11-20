namespace Application.DTOs.Athletes;

/// <summary>
/// Perfil físico del atleta usado por recomendaciones de hábitos.
/// <c>WeightKg</c> null = sin dato (nunca se inventa un peso por defecto).
/// <c>AthleticGoal</c> debe ser una de las etiquetas cerradas de AthleticGoalLabels;
/// null/vacío se normaliza a "Objetivo no informado".
/// </summary>
public sealed record UpdatePhysicalProfileRequest(
    decimal? WeightKg = null,
    string? AthleticGoal = null);
