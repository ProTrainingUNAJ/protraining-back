using Application.Common.Ai;
using Application.Common.Helpers;
using Application.DTOs.Ai;

namespace Infrastructure.Services.Ai.Rules;

/// <summary>
/// Sesión modelo de una semana antes de proyectar progresiones semanales (KAN-605).
/// </summary>
public sealed record RuleSessionBlueprint(
    string FocusName,
    IReadOnlyList<CatalogExerciseDto> Exercises,
    IReadOnlyList<CatalogExerciseDto> FallbackPool,
    IReadOnlyList<string> SubstitutionNotes);

/// <summary>
/// Resultado del reparto semanal armado por reglas (descripción legible + sesiones modelo).
/// </summary>
public sealed record RuleSplitPlan(
    string SplitDescription,
    IReadOnlyList<RuleSessionBlueprint> Sessions);

/// <summary>
/// Arma el reparto equilibrado de sesiones según días por semana o foco muscular explícito del PF (KAN-605).
/// Prioriza ejercicios multiarticulares compuestos al inicio de cada sesión sin repetir IDs dentro de la misma.
/// </summary>
public static class RuleBasedSplitPlanner
{
    private static readonly string[] PrimaryCompoundKeywords =
    [
        "sentadilla", "squat", "peso muerto", "deadlift", "press de banca", "press banca",
        "bench press", "press militar", "overhead press", "dominada", "pull-up", "pull up",
        "remo con barra", "barbell row", "cargada", "clean", "arranque", "snatch",
        "hip thrust", "empuje de cadera", "prensa", "leg press", "fondo", "dip",
        "salto al cajon", "box jump", "push press"
    ];

    private static readonly string[] SecondaryCompoundKeywords =
    [
        "press", "remo", "row", "zancada", "estocada", "lunge", "jalon", "pulldown",
        "flexion", "push up", "bulgara", "step-up", "subida al cajon", "swing", "thruster"
    ];

    public static RuleSplitPlan BuildSplit(
        IReadOnlyList<CatalogExerciseDto> catalog,
        string? goal,
        int daysPerWeek,
        int maxExercisesPerSession = 5)
    {
        if (catalog.Count == 0 || daysPerWeek <= 0)
            return new RuleSplitPlan("sin sesiones", []);

        var deduplicatedCatalog = catalog
            .Where(e => e.Id != Guid.Empty)
            .GroupBy(e => e.Id)
            .Select(g => g.First())
            .ToList();

        if (deduplicatedCatalog.Count == 0)
            return new RuleSplitPlan("sin sesiones", []);

        // 1. Si el PF pidió un foco muscular explícito (ej. "piernas", "tren superior", "espalda"), ese foco manda
        var focusedGroups = GoalMuscleFocusMapper.ExtractMuscleGroups(goal);
        if (focusedGroups.Count > 0)
        {
            var focusedPool = deduplicatedCatalog
                .Where(e => GoalMuscleFocusMapper.MatchesMuscleGroups(e, focusedGroups))
                .ToList();

            if (focusedPool.Count > 0)
            {
                return BuildFocusedSplit(focusedPool, focusedGroups, daysPerWeek, maxExercisesPerSession);
            }
        }

        // 2. Sin foco explícito: reparto equilibrado según DaysPerWeek
        return BuildBalancedSplit(deduplicatedCatalog, daysPerWeek, maxExercisesPerSession);
    }

    private static RuleSplitPlan BuildFocusedSplit(
        List<CatalogExerciseDto> focusedPool,
        IReadOnlyList<string> focusedGroups,
        int daysPerWeek,
        int maxExercisesPerSession)
    {
        var isUpperBodyFocus =
            !focusedGroups.Contains("Piernas", StringComparer.OrdinalIgnoreCase) &&
            focusedGroups.Contains("Pecho", StringComparer.OrdinalIgnoreCase) &&
            focusedGroups.Contains("Espalda", StringComparer.OrdinalIgnoreCase);

        if (isUpperBodyFocus)
        {
            var orderedAllUpper = OrderCompoundFirst(focusedPool);
            var upperPush = OrderCompoundFirst(focusedPool.Where(IsPushExercise).ToList());
            var upperPull = OrderCompoundFirst(focusedPool.Where(IsPullExercise).ToList());

            var sessions = new List<RuleSessionBlueprint>(daysPerWeek);
            for (var dayIndex = 0; dayIndex < daysPerWeek; dayIndex++)
            {
                if (daysPerWeek == 1 || upperPush.Count == 0 || upperPull.Count == 0)
                {
                    var selected = PickExercises(orderedAllUpper, maxExercisesPerSession, rotationOffset: dayIndex);
                    sessions.Add(new RuleSessionBlueprint("Tren superior", selected, orderedAllUpper, []));
                }
                else if (dayIndex % 2 == 0)
                {
                    var selected = PickExercises(upperPush, maxExercisesPerSession, rotationOffset: dayIndex / 2);
                    sessions.Add(new RuleSessionBlueprint("Tren superior · Empuje", selected, orderedAllUpper, []));
                }
                else
                {
                    var selected = PickExercises(upperPull, maxExercisesPerSession, rotationOffset: dayIndex / 2);
                    sessions.Add(new RuleSessionBlueprint("Tren superior · Tracción", selected, orderedAllUpper, []));
                }
            }

            return new RuleSplitPlan($"foco en tren superior ({daysPerWeek} {(daysPerWeek == 1 ? "día" : "días")}/semana)", sessions);
        }

        var orderedFocused = OrderCompoundFirst(focusedPool);
        var focusLabel = focusedGroups.Count == 1
            ? focusedGroups[0]
            : string.Join(" / ", focusedGroups);

        var focusedSessions = new List<RuleSessionBlueprint>(daysPerWeek);
        for (var dayIndex = 0; dayIndex < daysPerWeek; dayIndex++)
        {
            var selected = PickExercises(orderedFocused, maxExercisesPerSession, rotationOffset: dayIndex);
            focusedSessions.Add(new RuleSessionBlueprint(focusLabel, selected, orderedFocused, []));
        }

        return new RuleSplitPlan($"foco en {focusLabel.ToLowerInvariant()} ({daysPerWeek} {(daysPerWeek == 1 ? "día" : "días")}/semana)", focusedSessions);
    }

    private static RuleSplitPlan BuildBalancedSplit(
        List<CatalogExerciseDto> catalog,
        int daysPerWeek,
        int maxExercisesPerSession)
    {
        var (legs, push, pull, core, otherUpper) = PartitionCatalog(catalog);

        var orderedLegs = OrderCompoundFirst(legs);
        var orderedPush = OrderCompoundFirst(push);
        var orderedPull = OrderCompoundFirst(pull);
        var orderedCore = OrderCompoundFirst(core);
        var orderedAll = OrderCompoundFirst(catalog);

        var upperPool = InterleaveUnique(orderedPush, orderedPull, OrderCompoundFirst(otherUpper));
        var lowerPool = orderedLegs.Count > 0 ? orderedLegs : orderedAll;
        var fullBodyPool = InterleaveUnique(orderedLegs, orderedPush, orderedPull, orderedCore, orderedAll);

        var effectivePush = orderedPush.Count > 0 ? orderedPush : orderedAll;
        var effectivePull = orderedPull.Count > 0 ? orderedPull : orderedAll;
        var effectiveUpper = upperPool.Count > 0 ? upperPool : orderedAll;

        var slots = daysPerWeek switch
        {
            1 => new (string Focus, List<CatalogExerciseDto> Pool, int Rotation)[]
            {
                ("Cuerpo completo", fullBodyPool, 0)
            },
            2 =>
            [
                ("Cuerpo completo A", fullBodyPool, 0),
                ("Cuerpo completo B", fullBodyPool, 1)
            ],
            3 =>
            [
                ("Empuje", effectivePush, 0),
                ("Tracción", effectivePull, 0),
                ("Piernas", lowerPool, 0)
            ],
            4 =>
            [
                ("Tren superior", effectiveUpper, 0),
                ("Tren inferior", lowerPool, 0),
                ("Tren superior", effectiveUpper, 1),
                ("Tren inferior", lowerPool, 1)
            ],
            5 =>
            [
                ("Empuje", effectivePush, 0),
                ("Tracción", effectivePull, 0),
                ("Piernas", lowerPool, 0),
                ("Superior", effectiveUpper, 1),
                ("Inferior", lowerPool, 1)
            ],
            _ => BuildExtendedSlots(daysPerWeek, effectivePush, effectivePull, lowerPool, effectiveUpper, fullBodyPool)
        };

        var splitDescription = daysPerWeek switch
        {
            1 => "reparto de cuerpo completo (1 día/semana)",
            2 => "reparto de cuerpo completo A/B (2 días/semana)",
            3 => "reparto empuje, tracción y piernas (3 días/semana)",
            4 => "reparto alternado de tren superior e inferior (4 días/semana)",
            _ => $"reparto empuje, tracción, piernas, superior e inferior ({daysPerWeek} días/semana)"
        };

        var sessions = slots
            .Select(slot => new RuleSessionBlueprint(
                FocusName: slot.Focus,
                Exercises: PickExercises(slot.Pool, maxExercisesPerSession, slot.Rotation),
                FallbackPool: slot.Pool,
                SubstitutionNotes: []))
            .ToList();

        return new RuleSplitPlan(splitDescription, sessions);
    }

    private static (string Focus, List<CatalogExerciseDto> Pool, int Rotation)[] BuildExtendedSlots(
        int daysPerWeek,
        List<CatalogExerciseDto> push,
        List<CatalogExerciseDto> pull,
        List<CatalogExerciseDto> lower,
        List<CatalogExerciseDto> upper,
        List<CatalogExerciseDto> fullBody)
    {
        var template = new (string Focus, List<CatalogExerciseDto> Pool, int Rotation)[]
        {
            ("Empuje", push, 0),
            ("Tracción", pull, 0),
            ("Piernas", lower, 0),
            ("Superior", upper, 1),
            ("Inferior", lower, 1),
            ("Cuerpo completo", fullBody, 0)
        };

        var result = new (string Focus, List<CatalogExerciseDto> Pool, int Rotation)[daysPerWeek];
        for (var i = 0; i < daysPerWeek; i++)
        {
            var baseSlot = template[i % template.Length];
            result[i] = (baseSlot.Focus, baseSlot.Pool, baseSlot.Rotation + (i / template.Length));
        }

        return result;
    }

    private static (
        List<CatalogExerciseDto> Legs,
        List<CatalogExerciseDto> Push,
        List<CatalogExerciseDto> Pull,
        List<CatalogExerciseDto> Core,
        List<CatalogExerciseDto> OtherUpper)
        PartitionCatalog(IReadOnlyList<CatalogExerciseDto> catalog)
    {
        var legs = new List<CatalogExerciseDto>();
        var push = new List<CatalogExerciseDto>();
        var pull = new List<CatalogExerciseDto>();
        var core = new List<CatalogExerciseDto>();
        var otherUpper = new List<CatalogExerciseDto>();

        foreach (var ex in catalog)
        {
            if (IsLegsExercise(ex))
            {
                legs.Add(ex);
            }
            else if (IsPushExercise(ex))
            {
                push.Add(ex);
            }
            else if (IsPullExercise(ex))
            {
                pull.Add(ex);
            }
            else if (IsCoreExercise(ex))
            {
                core.Add(ex);
            }
            else if (IsGeneralUpperExercise(ex))
            {
                otherUpper.Add(ex);
            }
        }

        return (legs, push, pull, core, otherUpper);
    }

    private static bool IsLegsExercise(CatalogExerciseDto ex)
    {
        var mg = TextNormalizer.Normalize(ex.MuscleGroup);
        var name = TextNormalizer.Normalize(ex.Name);

        if (!string.IsNullOrEmpty(mg))
        {
            if (mg.Contains("pierna") || mg.Contains("cuadriceps") || mg.Contains("isquio") ||
                mg.Contains("gluteo") || mg.Contains("gemelo") || mg.Contains("pantorrilla") ||
                mg.Contains("lower body") || mg.Contains("tren inferior"))
            {
                return true;
            }

            // Si tiene grupo muscular explícito de otro tipo (ej. Espalda o Pecho), no reclasificar por nombre
            if (mg.Contains("pecho") || mg.Contains("espalda") || mg.Contains("hombro") ||
                mg.Contains("brazo") || mg.Contains("biceps") || mg.Contains("triceps") ||
                mg.Contains("core") || mg.Contains("abdom"))
            {
                return false;
            }
        }

        return name.Contains("sentadilla") || name.Contains("squat") || name.Contains("prensa") ||
               name.Contains("zancada") || name.Contains("estocada") || name.Contains("lunge") ||
               name.Contains("hip thrust") || name.Contains("empuje de cadera") ||
               name.Contains("salto al cajon") || name.Contains("box jump");
    }

    private static bool IsPushExercise(CatalogExerciseDto ex)
    {
        var mg = TextNormalizer.Normalize(ex.MuscleGroup);
        var name = TextNormalizer.Normalize(ex.Name);

        if (!string.IsNullOrEmpty(mg))
        {
            if (mg.Contains("pecho") || mg.Contains("pectoral") || mg.Contains("hombro") ||
                mg.Contains("deltoid") || mg.Contains("triceps") || mg.Contains("push") || mg.Contains("empuje"))
            {
                return true;
            }

            if (mg.Contains("espalda") || mg.Contains("dorsal") || mg.Contains("biceps") ||
                mg.Contains("pierna") || mg.Contains("core") || mg.Contains("abdom"))
            {
                return false;
            }
        }

        return name.Contains("press") || name.Contains("fondo") || name.Contains("dip") ||
               name.Contains("flexion") || name.Contains("push up") || name.Contains("apertura") ||
               name.Contains("triceps");
    }

    private static bool IsPullExercise(CatalogExerciseDto ex)
    {
        var mg = TextNormalizer.Normalize(ex.MuscleGroup);
        var name = TextNormalizer.Normalize(ex.Name);

        if (!string.IsNullOrEmpty(mg))
        {
            if (mg.Contains("espalda") || mg.Contains("dorsal") || mg.Contains("biceps") ||
                mg.Contains("trapecio") || mg.Contains("pull") || mg.Contains("traccion"))
            {
                return true;
            }

            if (mg.Contains("pecho") || mg.Contains("hombro") || mg.Contains("triceps") ||
                mg.Contains("pierna") || mg.Contains("core") || mg.Contains("abdom"))
            {
                return false;
            }
        }

        return name.Contains("remo") || name.Contains("row") || name.Contains("dominada") ||
               name.Contains("pull-up") || name.Contains("pull up") || name.Contains("jalon") ||
               name.Contains("peso muerto") || name.Contains("deadlift") || name.Contains("curl de biceps") ||
               name.Contains("face pull");
    }

    private static bool IsCoreExercise(CatalogExerciseDto ex)
    {
        var mg = TextNormalizer.Normalize(ex.MuscleGroup);
        var name = TextNormalizer.Normalize(ex.Name);

        return mg.Contains("core") || mg.Contains("abdom") || mg.Contains("zona media") ||
               name.Contains("plancha") || name.Contains("abdominal") || name.Contains("pallof") || name.Contains("deadbug");
    }

    private static bool IsGeneralUpperExercise(CatalogExerciseDto ex)
    {
        var mg = TextNormalizer.Normalize(ex.MuscleGroup);
        return mg.Contains("tren superior") || mg.Contains("upper body") || mg.Contains("brazo");
    }

    private static List<CatalogExerciseDto> OrderCompoundFirst(IReadOnlyList<CatalogExerciseDto> exercises) =>
        exercises
            .OrderBy(GetCompoundPriority)
            .ToList();

    private static int GetCompoundPriority(CatalogExerciseDto exercise)
    {
        var name = TextNormalizer.Normalize(exercise.Name);
        if (PrimaryCompoundKeywords.Any(name.Contains))
            return 0;

        if (SecondaryCompoundKeywords.Any(name.Contains))
            return 1;

        return 2;
    }

    private static List<CatalogExerciseDto> InterleaveUnique(params IReadOnlyList<CatalogExerciseDto>[] pools)
    {
        var queues = pools
            .Where(p => p.Count > 0)
            .Select(p => new Queue<CatalogExerciseDto>(p))
            .ToList();

        var result = new List<CatalogExerciseDto>();
        var seen = new HashSet<Guid>();

        bool added;
        do
        {
            added = false;
            foreach (var queue in queues)
            {
                while (queue.Count > 0)
                {
                    var candidate = queue.Dequeue();
                    if (seen.Add(candidate.Id))
                    {
                        result.Add(candidate);
                        added = true;
                        break;
                    }
                }
            }
        }
        while (added);

        return result;
    }

    private static List<CatalogExerciseDto> PickExercises(
        IReadOnlyList<CatalogExerciseDto> pool,
        int maxCount,
        int rotationOffset)
    {
        if (pool.Count == 0 || maxCount <= 0)
            return [];

        var count = Math.Min(maxCount, pool.Count);
        if (rotationOffset == 0 || pool.Count <= count)
            return pool.Take(count).ToList();

        // Rotar por bloques/paso suave para que días alternos (A/B o Superior 1/2) varíen ejercicios
        // manteniendo siempre un ejercicio compuesto en la primera posición
        var shift = (rotationOffset * 2) % pool.Count;
        var rotated = new List<CatalogExerciseDto>(count);
        for (var i = 0; i < count; i++)
        {
            rotated.Add(pool[(shift + i) % pool.Count]);
        }

        return OrderCompoundFirst(rotated);
    }
}
