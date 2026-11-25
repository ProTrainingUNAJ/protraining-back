using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.Seeders;

// Siembra ejercicios de movilidad, activación y entrada en calor y estandariza
// la clasificación de grupos musculares de los ejercicios básicos (KAN-590).
// Corre en cada arranque y es completamente idempotente.
public class MobilityAndBasicExerciseSeeder
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        await ReclassifyBasicsAsync(context, logger);
        await SeedMobilityAndWarmupExercisesAsync(context, logger);
    }

    private static async Task ReclassifyBasicsAsync(AppDbContext context, ILogger logger)
    {
        var catalogExercises = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null)
            .ToListAsync();

        var updatedCount = 0;

        foreach (var exercise in catalogExercises)
        {
            var sourceId = ExerciseNameSyncSeeder.SourceIdFromImages(exercise.Images);
            var targetMuscleGroup = ExerciseClassificationHelper.DetermineBasicMuscleGroup(exercise.Name, sourceId);

            if (targetMuscleGroup != null && !string.Equals(exercise.MuscleGroup, targetMuscleGroup, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("KAN-590: Reclasificando ejercicio {Name}: {OldGroup} -> {NewGroup}",
                    exercise.Name, exercise.MuscleGroup, targetMuscleGroup);
                exercise.UpdateMuscleGroup(targetMuscleGroup);
                updatedCount++;
            }
        }

        if (updatedCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("KAN-590: Se reclasificaron {Count} ejercicios del catálogo.", updatedCount);
        }
    }

    private static async Task SeedMobilityAndWarmupExercisesAsync(AppDbContext context, ILogger logger)
    {
        var existingExercises = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null)
            .ToListAsync();

        var existingByName = existingExercises
            .GroupBy(e => e.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var exercisesToAdd = new List<Exercise>();
        var updatedExistingCount = 0;

        void AddOrUpdate(string name, string muscleGroup, string equipment, ExerciseType exerciseType, List<string> primaryMuscles, string description)
        {
            if (existingByName.TryGetValue(name.ToLowerInvariant(), out var existing))
            {
                if (!string.Equals(existing.MuscleGroup, muscleGroup, StringComparison.OrdinalIgnoreCase))
                {
                    existing.UpdateMuscleGroup(muscleGroup);
                    updatedExistingCount++;
                }

                if (existing.ExerciseType != exerciseType)
                {
                    existing.UpdateExerciseType(exerciseType);
                    updatedExistingCount++;
                }

                return;
            }

            exercisesToAdd.Add(Exercise.Create(
                name: name,
                muscleGroup: muscleGroup,
                description: description,
                equipment: equipment,
                primaryMuscles: primaryMuscles,
                origin: Origin.Catalog,
                exerciseType: exerciseType
            ));
        }

        // 1. Movilidad de cadera 90/90
        AddOrUpdate(
            name: "Movilidad de cadera 90/90",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["glúteos", "abductores", "aductores"],
            description: "Sentado en el suelo con ambas piernas flexionadas a 90 grados, rotá las caderas alternando hacia cada lado manteniendo el torso erguido."
        );

        // 2. Movilidad de tobillo en pared
        AddOrUpdate(
            name: "Movilidad de tobillo en pared",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["gemelos"],
            description: "Colocá la punta del pie a unos centímetros de la pared y flexioná la rodilla hacia adelante buscando tocar la pared sin levantar el talón."
        );

        // 3. Gato-camello (movilidad de columna)
        AddOrUpdate(
            name: "Gato-camello (movilidad de columna)",
            muscleGroup: "Core",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["abdominales", "espalda baja"],
            description: "En posición de cuadrupedia, arqueá la columna hacia arriba bajando la cabeza (gato) y luego extendela levantando el pecho y la mirada (camello)."
        );

        // 4. Rotaciones torácicas (movilidad)
        AddOrUpdate(
            name: "Rotaciones torácicas (movilidad)",
            muscleGroup: "Espalda",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["espalda media"],
            description: "En posición de cuadrupedia o de rodillas con una mano tras la nuca, rotá el torso abriendo el codo hacia el techo y luego bajalo buscando el codo opuesto."
        );

        // 5. "World's greatest stretch" (estocada con rotación, movilidad)
        AddOrUpdate(
            name: "\"World's greatest stretch\" (estocada con rotación, movilidad)",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["isquiotibiales", "glúteos", "cuádriceps"],
            description: "Realizá una estocada profunda, apoyá la mano contraria en el suelo y rotá el torso levantando el brazo del lado adelantado hacia el techo."
        );

        // 6. Dislocaciones de hombro con palo o banda (movilidad)
        AddOrUpdate(
            name: "Dislocaciones de hombro con palo o banda (movilidad)",
            muscleGroup: "Hombros",
            equipment: "banda",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["hombros"],
            description: "Sujetá un palo o banda con agarre amplio frente a vos y llevalo por encima de la cabeza hacia la espalda con los brazos estirados sin forzar."
        );

        // 7. Puente de glúteo (activación)
        AddOrUpdate(
            name: "Puente de glúteo (activación)",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["glúteos", "isquiotibiales"],
            description: "Acostate boca arriba con las rodillas flexionadas y los pies apoyados en el suelo. Elevá la cadera contrayendo los glúteos hasta alinear muslos y torso."
        );

        // 8. Caminata lateral con banda (monster walk, activación)
        AddOrUpdate(
            name: "Caminata lateral con banda (monster walk, activación)",
            muscleGroup: "Piernas",
            equipment: "banda",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["abductores", "glúteos"],
            description: "Con una banda circular alrededor de las piernas, flexioná levemente las rodillas y caminá con pasos laterales controlados manteniendo la tensión."
        );

        // 9. Face pull con banda (activación)
        AddOrUpdate(
            name: "Face pull con banda (activación)",
            muscleGroup: "Hombros",
            equipment: "banda",
            exerciseType: ExerciseType.Reps,
            primaryMuscles: ["hombros", "trapecios"],
            description: "Sujetá la banda con ambas manos a la altura del rostro y tirá hacia atrás separando los codos hasta llevar las manos a los costados de la cabeza."
        );

        // 10. Plancha (activación)
        AddOrUpdate(
            name: "Plancha (activación)",
            muscleGroup: "Core",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Time,
            primaryMuscles: ["abdominales"],
            description: "Mantené el cuerpo en línea recta apoyándote sobre los antebrazos y las puntas de los pies, contrayendo el abdomen y los glúteos sin arquear la espalda."
        );

        // 11. Saltos a la soga (entrada en calor)
        AddOrUpdate(
            name: "Saltos a la soga (entrada en calor)",
            muscleGroup: "Piernas",
            equipment: "cuerda",
            exerciseType: ExerciseType.Time,
            primaryMuscles: ["gemelos", "cuádriceps"],
            description: "Realizá saltos continuos a la soga con ritmo constante, amortiguando con la punta de los pies y manteniendo los codos cerca del cuerpo."
        );

        // 12. Jumping jacks (entrada en calor)
        AddOrUpdate(
            name: "Jumping jacks (entrada en calor)",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Time,
            primaryMuscles: ["gemelos", "cuádriceps", "abductores"],
            description: "Parate con los pies juntos y los brazos a los lados. Saltá abriendo las piernas y llevando los brazos por encima de la cabeza, y volvé a la posición inicial."
        );

        // 13. Trote suave o bicicleta (5 min, entrada en calor)
        AddOrUpdate(
            name: "Trote suave o bicicleta (5 min, entrada en calor)",
            muscleGroup: "Piernas",
            equipment: "solo el cuerpo",
            exerciseType: ExerciseType.Time,
            primaryMuscles: ["cuádriceps", "gemelos"],
            description: "Entrada en calor aeróbica de 5 minutos mediante trote suave o pedaleo a ritmo continuo y moderado para elevar la temperatura corporal."
        );

        if (exercisesToAdd.Count > 0)
        {
            await context.Exercises.AddRangeAsync(exercisesToAdd);
        }

        if (exercisesToAdd.Count > 0 || updatedExistingCount > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("KAN-590: Se agregaron {AddedCount} y se actualizaron {UpdatedCount} ejercicios de movilidad/activación/entrada en calor en el catálogo.",
                exercisesToAdd.Count, updatedExistingCount);
        }
    }
}
