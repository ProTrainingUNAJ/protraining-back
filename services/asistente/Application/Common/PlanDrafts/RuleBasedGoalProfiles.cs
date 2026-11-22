using Application.Common.Helpers;

namespace Infrastructure.Services.Ai.Rules;

/// <summary>
/// Categoría deportiva detectada a partir del texto del objetivo del PF (KAN-605).
/// </summary>
public enum RuleTrainingGoal
{
    Strength,
    Hypertrophy,
    Power,
    Endurance,
    General
}

/// <summary>
/// Prescripción base por objetivo para el motor determinístico de reglas (KAN-605).
/// Centraliza series, repeticiones, %RM (base y tope) y descansos en una tabla única.
/// </summary>
public sealed record RuleGoalProfile(
    RuleTrainingGoal Goal,
    string DisplayName,
    IReadOnlyList<string> Keywords,
    int MainSets,
    int MainReps,
    decimal MainBasePercentage,
    decimal MainMaxPercentage,
    int AccessorySets,
    int AccessoryReps,
    decimal AccessoryBasePercentage,
    string RestNote,
    string? ExecutionCue = null);

/// <summary>
/// Prescripción calculada para un ejercicio según su orden dentro de la sesión.
/// </summary>
public readonly record struct RuleExercisePrescription(
    int Sets,
    int Reps,
    decimal BasePercentage,
    decimal MaxPercentage,
    bool IsMain);

/// <summary>
/// Tabla determinística de objetivos deportivos y detección por palabras clave en español e inglés (KAN-605).
/// </summary>
public static class RuleBasedGoalProfiles
{
    // Orden de prioridad: potencia y resistencia antes de hipertrofia y fuerza para que frases como
    // "fuerza explosiva" o "resistencia muscular" no caigan en fuerza o hipertrofia.
    private static readonly RuleTrainingGoal[] DetectionOrder =
    [
        RuleTrainingGoal.Power,
        RuleTrainingGoal.Endurance,
        RuleTrainingGoal.Hypertrophy,
        RuleTrainingGoal.Strength
    ];

    // Referencias bibliográficas de la tabla de prescripción (decisión PM · KAN-605):
    // 1. NSCA — Haff, G. G. & Triplett, N. T. (4.ª ed.), "Essentials of Strength Training and Conditioning",
    //    tabla "Asignación de carga y repeticiones según el objetivo":
    //    - Fuerza: >=85 % del 1RM, <=6 reps, 2–6 series, 2–5 min de pausa (4×5 al 85 %, tope 87,5 %, pausa 3 min).
    //    - Potencia (esfuerzo múltiple en ejercicios clásicos): 75–85 %, 3–5 reps, 3–5 series, 2–5 min (4×3 al 75–85 %, explosivo, pausa 3 min).
    //    - Hipertrofia: 67–85 %, 6–12 reps, 30–90 s de pausa (4×10 al 70–75 %, pausa 90 s).
    //    - Resistencia muscular: <=67 %, >=12 reps, 2–3 series, <=30 s de pausa (3×15 al 50–55 %, pausa 30 s).
    // 2. Cormie, P., McGuigan, M. R. & Newton, R. U. (2011), Sports Medicine 41(2):
    //    - Potencia (accesorios balísticos / explosivos): 0–50 % del 1RM (3×6 al 45 %).
    // 3. ACSM (2009), Medicine & Science in Sports & Exercise 41(3), Position Stand "Progression Models in Resistance Training":
    //    - General / principiantes: 60–70 % del 1RM, 8–12 reps (3×10 al 65–70 %, pausa 90 s).
    //    - Progresión semanal: incrementos de 2–10 % cuando se superan las repeticiones objetivo (+2,5 % por semana).
    public static readonly IReadOnlyDictionary<RuleTrainingGoal, RuleGoalProfile> Profiles =
        new Dictionary<RuleTrainingGoal, RuleGoalProfile>
        {
            [RuleTrainingGoal.Strength] = new(
                Goal: RuleTrainingGoal.Strength,
                DisplayName: "fuerza",
                Keywords: ["fuerza", "maxima", "strength", "heavy"],
                MainSets: 4,
                MainReps: 5,
                MainBasePercentage: 85m,
                MainMaxPercentage: 87.5m,
                AccessorySets: 3,
                AccessoryReps: 8,
                AccessoryBasePercentage: 70m,
                RestNote: "3 min"),

            [RuleTrainingGoal.Hypertrophy] = new(
                Goal: RuleTrainingGoal.Hypertrophy,
                DisplayName: "hipertrofia",
                Keywords: ["hipertrofia", "masa", "masa muscular", "desarrollo muscular", "ganancia muscular", "volumen", "hypertrophy", "muscle mass", "muscle growth", "volume"],
                MainSets: 4,
                MainReps: 10,
                MainBasePercentage: 70m,
                MainMaxPercentage: 75m,
                AccessorySets: 3,
                AccessoryReps: 12,
                AccessoryBasePercentage: 60m,
                RestNote: "90 s"),

            [RuleTrainingGoal.Power] = new(
                Goal: RuleTrainingGoal.Power,
                DisplayName: "potencia",
                Keywords: ["potencia", "explosiv", "salto", "velocidad", "power", "speed", "jump", "pliometr", "plyo"],
                MainSets: 4,
                MainReps: 3,
                MainBasePercentage: 75m,
                MainMaxPercentage: 85m,
                AccessorySets: 3,
                AccessoryReps: 6,
                AccessoryBasePercentage: 45m,
                RestNote: "3 min",
                ExecutionCue: "explosivo"),

            [RuleTrainingGoal.Endurance] = new(
                Goal: RuleTrainingGoal.Endurance,
                DisplayName: "resistencia",
                Keywords: ["resistencia", "aerobic", "tonific", "endurance", "stamina", "cardio", "metabolic", "conditioning"],
                MainSets: 3,
                MainReps: 15,
                MainBasePercentage: 50m,
                MainMaxPercentage: 55m,
                AccessorySets: 2,
                AccessoryReps: 18,
                AccessoryBasePercentage: 45m,
                RestNote: "30 s"),

            [RuleTrainingGoal.General] = new(
                Goal: RuleTrainingGoal.General,
                DisplayName: "general",
                Keywords: [],
                MainSets: 3,
                MainReps: 10,
                MainBasePercentage: 65m,
                MainMaxPercentage: 70m,
                AccessorySets: 2,
                AccessoryReps: 12,
                AccessoryBasePercentage: 55m,
                RestNote: "90 s")
        };

    /// <summary>
    /// Detecta el perfil deportivo del texto del objetivo ignorando tildes y mayúsculas.
    /// </summary>
    public static RuleGoalProfile Detect(string? goal)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return Profiles[RuleTrainingGoal.General];

        var normalized = TextNormalizer.Normalize(goal);
        foreach (var goalType in DetectionOrder)
        {
            var profile = Profiles[goalType];
            if (profile.Keywords.Any(normalized.Contains))
                return profile;
        }

        return Profiles[RuleTrainingGoal.General];
    }

    /// <summary>
    /// Devuelve series, repeticiones y %RM según el orden del ejercicio en la sesión:
    /// el compuesto principal (1.° y 2.°) usa la fila principal de la tabla; del 3.° en adelante son accesorios.
    /// </summary>
    public static RuleExercisePrescription GetPrescription(RuleGoalProfile profile, int exerciseOrder)
    {
        if (exerciseOrder <= 2)
        {
            return new RuleExercisePrescription(
                Sets: profile.MainSets,
                Reps: profile.MainReps,
                BasePercentage: profile.MainBasePercentage,
                MaxPercentage: profile.MainMaxPercentage,
                IsMain: true);
        }

        return new RuleExercisePrescription(
            Sets: profile.AccessorySets,
            Reps: profile.AccessoryReps,
            BasePercentage: profile.AccessoryBasePercentage,
            MaxPercentage: profile.AccessoryBasePercentage,
            IsMain: false);
    }
}
