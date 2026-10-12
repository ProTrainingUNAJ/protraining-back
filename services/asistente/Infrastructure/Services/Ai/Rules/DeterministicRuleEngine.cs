using Application.DTOs.Ai;

namespace Infrastructure.Services.Ai.Rules;

/// <summary>
/// Deterministic rule engine that creates structured training proposals based on
/// sports science principles when external AI providers are unavailable.
/// </summary>
public static class DeterministicRuleEngine
{
    public static (string Title, string Justification, List<SuggestedExerciseDto> Exercises) GenerateProposal(
        string goal,
        List<string>? muscleGroupEmphasis)
    {
        var category = ClassifyGoal(goal);
        var targetArea = ClassifyTargetArea(muscleGroupEmphasis);

        var title = BuildTitle(category, targetArea, goal);
        var justification = BuildJustification(category, targetArea, goal);
        var exercises = BuildExercises(category, targetArea);

        return (title, justification, exercises);
    }

    private static GoalCategory ClassifyGoal(string goal)
    {
        var normalized = goal.ToLowerInvariant();

        if (normalized.Contains("descarga") ||
            normalized.Contains("recuperac") ||
            normalized.Contains("movilidad") ||
            normalized.Contains("molestia") ||
            normalized.Contains("dolor") ||
            normalized.Contains("fatiga"))
        {
            return GoalCategory.DeloadAndRecovery;
        }

        if (normalized.Contains("potencia") ||
            normalized.Contains("explosiv") ||
            normalized.Contains("salto") ||
            normalized.Contains("velocidad"))
        {
            return GoalCategory.Power;
        }

        if (normalized.Contains("hipertrofia") ||
            normalized.Contains("masa") ||
            normalized.Contains("volumen") ||
            normalized.Contains("muscular"))
        {
            return GoalCategory.Hypertrophy;
        }

        if (normalized.Contains("resistencia") ||
            normalized.Contains("condicionamiento") ||
            normalized.Contains("metabolic") ||
            normalized.Contains("cardio"))
        {
            return GoalCategory.Endurance;
        }

        if (normalized.Contains("fuerza"))
        {
            return GoalCategory.MaxStrength;
        }

        return GoalCategory.GeneralConditioning;
    }

    private static TargetArea ClassifyTargetArea(List<string>? emphasis)
    {
        if (emphasis == null || emphasis.Count == 0)
            return TargetArea.FullBody;

        var text = string.Join(" ", emphasis).ToLowerInvariant();

        var hasLegs = text.Contains("pierna") || text.Contains("cuadriceps") ||
                      text.Contains("isquio") || text.Contains("gluteo") ||
                      text.Contains("cadera") || text.Contains("tren inferior");

        var hasUpper = text.Contains("pecho") || text.Contains("espalda") ||
                       text.Contains("hombro") || text.Contains("brazo") ||
                       text.Contains("biceps") || text.Contains("triceps") ||
                       text.Contains("tren superior");

        var hasCore = text.Contains("core") || text.Contains("abdomen") ||
                      text.Contains("zona media") || text.Contains("lumbar");

        if (hasCore && !hasLegs && !hasUpper)
            return TargetArea.CoreOnly;

        if (hasLegs && !hasUpper)
            return TargetArea.LowerBody;

        if (hasUpper && !hasLegs)
            return TargetArea.UpperBody;

        return TargetArea.FullBody;
    }

    private static string BuildTitle(GoalCategory category, TargetArea targetArea, string rawGoal)
    {
        var areaLabel = targetArea switch
        {
            TargetArea.LowerBody => "Tren Inferior",
            TargetArea.UpperBody => "Tren Superior",
            TargetArea.CoreOnly => "Core y Zona Media",
            _ => "Cuerpo Completo (Full-Body)"
        };

        var categoryLabel = category switch
        {
            GoalCategory.MaxStrength => "Fuerza Máxima",
            GoalCategory.Hypertrophy => "Hipertrofia y Desarrollo Muscular",
            GoalCategory.Power => "Potencia y Velocidad",
            GoalCategory.Endurance => "Resistencia y Capacidad Metabólica",
            GoalCategory.DeloadAndRecovery => "Descarga Activa y Recuperación",
            _ => "Acondicionamiento Físico General"
        };

        return $"Plan Determinístico: {categoryLabel} ({areaLabel})";
    }

    private static string BuildJustification(GoalCategory category, TargetArea targetArea, string rawGoal)
    {
        var rationale = category switch
        {
            GoalCategory.MaxStrength =>
                "Se aplicaron rangos de 4 a 6 repeticiones con cargas del 75-85% de 1RM para maximizar la tensión mecánica y el reclutamiento motor, priorizando movimientos multiarticulares básicos.",

            GoalCategory.Hypertrophy =>
                "Se estructuraron bloques de 8 a 12 repeticiones al 65-75% de 1RM con volumen moderado-alto para favorecer el estrés metabólico y el estímulo hipertrófico.",

            GoalCategory.Power =>
                "Se seleccionaron ejercicios con aceleración balística y pliometría al 50-65% de 1RM, buscando máxima velocidad de ejecución y producción de fuerza en unidad de tiempo.",

            GoalCategory.Endurance =>
                "Se diseñó un circuito de 12 a 15 repeticiones con cargas ligeras (45-60%) y densidades altas para sostener la eficiencia glucolítica y la tolerancia a la fatiga.",

            GoalCategory.DeloadAndRecovery =>
                "Se redujo la intensidad al 40-50% con ejercicios de movilidad, activación de zona media y bajo impacto osteoarticular para disipar fatiga y prevenir sobrecargas.",

            _ =>
                "Se aplicó una plantilla balanceada de acondicionamiento físico funcional con cargas submáximas seguras (60-70%)."
        };

        return $"Propuesta generada mediante motor de reglas determinísticas (plantilla basada en objetivo: '{rawGoal}'). {rationale} Criterio activado como respaldo ante indisponibilidad de proveedores de IA.";
    }

    private static List<SuggestedExerciseDto> BuildExercises(GoalCategory category, TargetArea targetArea)
    {
        if (targetArea == TargetArea.CoreOnly)
        {
            return
            [
                new SuggestedExerciseDto("Plancha frontal isométrica", 3, 45, 0.0m),
                new SuggestedExerciseDto("Rueda abdominal (Ab rollouts)", 3, 10, 0.0m),
                new SuggestedExerciseDto("Press Pallof con banda elástica", 3, 12, 0.0m),
                new SuggestedExerciseDto("Deadbug controlado con respiración diafragmática", 3, 10, 0.0m)
            ];
        }

        return (category, targetArea) switch
        {
            // --- MAXIMUM STRENGTH ---
            (GoalCategory.MaxStrength, TargetArea.LowerBody) =>
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra (Back Squat)", 4, 5, 82.5m),
                new SuggestedExerciseDto("Peso muerto rumano con barra", 4, 6, 77.5m),
                new SuggestedExerciseDto("Prensa de piernas 45°", 3, 8, 72.5m),
                new SuggestedExerciseDto("Plancha isométrica con sobrecarga", 3, 30, 0.0m)
            ],
            (GoalCategory.MaxStrength, TargetArea.UpperBody) =>
            [
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 5, 82.5m),
                new SuggestedExerciseDto("Remo con barra Pendlay", 4, 6, 80.0m),
                new SuggestedExerciseDto("Press militar de pie con barra", 3, 6, 75.0m),
                new SuggestedExerciseDto("Dominadas con lastre o agarre prono", 3, 6, 75.0m)
            ],
            (GoalCategory.MaxStrength, TargetArea.FullBody) =>
            [
                new SuggestedExerciseDto("Sentadilla trasera con barra", 4, 5, 80.0m),
                new SuggestedExerciseDto("Press de banca plano con barra", 4, 5, 80.0m),
                new SuggestedExerciseDto("Peso muerto convencional o rumano", 3, 5, 80.0m),
                new SuggestedExerciseDto("Remo con barra inclinado", 3, 6, 75.0m)
            ],

            // --- HYPERTROPHY ---
            (GoalCategory.Hypertrophy, TargetArea.LowerBody) =>
            [
                new SuggestedExerciseDto("Sentadilla búlgara con mancuernas", 3, 10, 65.0m),
                new SuggestedExerciseDto("Prensa de pierna inclinada", 4, 10, 70.0m),
                new SuggestedExerciseDto("Curl femoral acostado en máquina", 3, 12, 65.0m),
                new SuggestedExerciseDto("Elevación de talones de pie (Gemelos)", 4, 15, 60.0m)
            ],
            (GoalCategory.Hypertrophy, TargetArea.UpperBody) =>
            [
                new SuggestedExerciseDto("Press de banca inclinado con mancuernas", 4, 10, 70.0m),
                new SuggestedExerciseDto("Remo en polea baja con agarre neutro", 4, 10, 70.0m),
                new SuggestedExerciseDto("Elevaciones laterales con mancuernas", 3, 12, 60.0m),
                new SuggestedExerciseDto("Fondos en paralelas o flexiones diamante", 3, 12, 0.0m)
            ],
            (GoalCategory.Hypertrophy, TargetArea.FullBody) =>
            [
                new SuggestedExerciseDto("Sentadilla Goblet con mancuerna", 3, 10, 65.0m),
                new SuggestedExerciseDto("Press de banca plano con mancuernas", 3, 10, 70.0m),
                new SuggestedExerciseDto("Remo unilateral con mancuerna", 3, 10, 65.0m),
                new SuggestedExerciseDto("Empuje de cadera (Hip Thrust) con barra", 3, 12, 65.0m)
            ],

            // --- POWER ---
            (GoalCategory.Power, TargetArea.LowerBody) =>
            [
                new SuggestedExerciseDto("Saltos al cajón pliométricos (Box Jumps)", 4, 5, 0.0m),
                new SuggestedExerciseDto("Sentadilla con salto y pausa excéntrica", 4, 5, 50.0m),
                new SuggestedExerciseDto("Swings con pesa rusa (Kettlebell)", 4, 10, 55.0m),
                new SuggestedExerciseDto("Zancadas alternadas dinámicas", 3, 6, 0.0m)
            ],
            (GoalCategory.Power, TargetArea.UpperBody) =>
            [
                new SuggestedExerciseDto("Lanzamiento de balón medicinal al pecho", 4, 6, 0.0m),
                new SuggestedExerciseDto("Press de banca dinámico a velocidad submáxima", 4, 5, 55.0m),
                new SuggestedExerciseDto("Dominadas explosivas al pecho", 3, 5, 0.0m),
                new SuggestedExerciseDto("Flexiones pliométricas con palmada", 3, 6, 0.0m)
            ],
            (GoalCategory.Power, TargetArea.FullBody) =>
            [
                new SuggestedExerciseDto("Cargada de potencia colgante (Hang Power Clean)", 4, 4, 60.0m),
                new SuggestedExerciseDto("Saltos al cajón (Box Jumps)", 4, 5, 0.0m),
                new SuggestedExerciseDto("Push Press con barra", 4, 5, 60.0m),
                new SuggestedExerciseDto("Lanzamiento de balón medicinal hacia el piso (Slams)", 3, 8, 0.0m)
            ],

            // --- ENDURANCE / METABOLIC CONDITIONING ---
            (GoalCategory.Endurance, TargetArea.LowerBody) =>
            [
                new SuggestedExerciseDto("Zancadas caminando continuas", 3, 16, 45.0m),
                new SuggestedExerciseDto("Sentadilla Goblet continua", 3, 15, 50.0m),
                new SuggestedExerciseDto("Subidas al cajón dinámicas (Step-ups)", 3, 15, 0.0m),
                new SuggestedExerciseDto("Puente de glúteos unilateral", 3, 15, 0.0m)
            ],
            (GoalCategory.Endurance, TargetArea.UpperBody) =>
            [
                new SuggestedExerciseDto("Flexiones de brazos tradicionales", 3, 15, 0.0m),
                new SuggestedExerciseDto("Remo en suspensión (TRX / Anillas)", 3, 15, 0.0m),
                new SuggestedExerciseDto("Press de hombro con mancuernas livianas", 3, 15, 45.0m),
                new SuggestedExerciseDto("Fondos en banco plano", 3, 15, 0.0m)
            ],
            (GoalCategory.Endurance, TargetArea.FullBody) =>
            [
                new SuggestedExerciseDto("Thrusters con mancuernas livianas", 3, 15, 45.0m),
                new SuggestedExerciseDto("Remo con mancuernas en posición de plancha (Renegade Row)", 3, 12, 45.0m),
                new SuggestedExerciseDto("Zancadas alternadas dinámicas", 3, 16, 0.0m),
                new SuggestedExerciseDto("Mountain Climbers continuos", 3, 30, 0.0m)
            ],

            // --- DELOAD / ACTIVE RECOVERY ---
            (GoalCategory.DeloadAndRecovery, _) =>
            [
                new SuggestedExerciseDto("Movilidad pasiva y activa de cadera (90/90)", 3, 10, 0.0m),
                new SuggestedExerciseDto("Sentadilla suave sin carga para rango articular", 3, 10, 0.0m),
                new SuggestedExerciseDto("Tracción facial con banda elástica (Face Pull)", 3, 12, 35.0m),
                new SuggestedExerciseDto("Deadbug y activación de respiración diafragmática", 3, 10, 0.0m)
            ],

            // --- GENERAL CONDITIONING / DEFAULT ---
            _ =>
            [
                new SuggestedExerciseDto("Sentadilla Goblet con mancuerna", 3, 10, 60.0m),
                new SuggestedExerciseDto("Press de banca con mancuernas", 3, 10, 65.0m),
                new SuggestedExerciseDto("Remo sentado en polea", 3, 10, 65.0m),
                new SuggestedExerciseDto("Plancha isométrica frontal", 3, 30, 0.0m)
            ]
        };
    }

    private enum GoalCategory
    {
        MaxStrength,
        Hypertrophy,
        Power,
        Endurance,
        DeloadAndRecovery,
        GeneralConditioning
    }

    private enum TargetArea
    {
        LowerBody,
        UpperBody,
        CoreOnly,
        FullBody
    }
}
