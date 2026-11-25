namespace Domain.Entities;

/// <summary>
/// Objetivo deportivo canónico del atleta. Vocabulario cerrado: no se admite texto libre
/// porque el motor de recomendaciones de hábitos decide el modo según Marcadores en español y bloquea
/// expresamente objetivos de pérdida de peso.
/// </summary>
public enum AthleticGoal
{
    GainMuscle = 1,
    Maintain = 2,
    Performance = 3,
    NotReported = 4
}

/// <summary>
/// Etiquetas canónicas en español. Se exponen en los DTO para que el consumidor (Asistente) pueda
/// pasar el texto directamente a sus reglas sin duplicar traducciones.
/// </summary>
public static class AthleticGoalLabels
{
    public const string GainMuscle = "Ganar masa muscular";
    public const string Maintain = "Mantener";
    public const string Performance = "Rendimiento deportivo";
    public const string NotReported = "Objetivo no informado";

    private static readonly IReadOnlyDictionary<AthleticGoal, string> Labels =
        new Dictionary<AthleticGoal, string>
        {
            [AthleticGoal.GainMuscle] = GainMuscle,
            [AthleticGoal.Maintain] = Maintain,
            [AthleticGoal.Performance] = Performance,
            [AthleticGoal.NotReported] = NotReported
        };

    public static string ToLabel(AthleticGoal? goal) =>
        goal.HasValue && Labels.TryGetValue(goal.Value, out var label) ? label : NotReported;

    /// <summary>
    /// Parseo estricto: una etiqueta no reconocida se rechaza para no inventar objetivos.
    /// null/vacío se normaliza a "no informado".
    /// </summary>
    public static bool TryParse(string? label, out AthleticGoal goal)
    {
        goal = AthleticGoal.NotReported;
        if (string.IsNullOrWhiteSpace(label))
            return true;

        var clean = label.Trim();
        foreach (var pair in Labels)
        {
            if (string.Equals(pair.Value, clean, StringComparison.OrdinalIgnoreCase))
            {
                goal = pair.Key;
                return true;
            }
        }

        return false;
    }

    public static string ValidValues => string.Join(", ", Labels.Values);
}
