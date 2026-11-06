using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Data.Seeders;

// Siembra ejercicios básicos de carrera, resistencia y saltos si no existen.
public class EnduranceExerciseSeeder
{
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        // Traemos los nombres existentes en catálogo para que la operación sea idempotente
        var existingNames = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null)
            .Select(e => e.Name.ToLower())
            .ToListAsync();

        var exercisesToAdd = new List<Exercise>();

        // 1. Pasadas (Distancia)
        if (!existingNames.Contains("pasadas"))
        {
            exercisesToAdd.Add(Exercise.Create(
                name: "Pasadas",
                muscleGroup: "Piernas",
                description: "Trabajo fraccionado de carrera por tramos o distancias establecidas.",
                equipment: "solo el cuerpo",
                primaryMuscles: ["cuádriceps", "gemelos", "isquiotibiales"],
                origin: Origin.Catalog,
                exerciseType: ExerciseType.Distance
            ));
        }

        // 2. Sprints (Distancia)
        if (!existingNames.Contains("sprints"))
        {
            exercisesToAdd.Add(Exercise.Create(
                name: "Sprints",
                muscleGroup: "Piernas",
                description: "Carrera a máxima aceleración y velocidad sobre distancia fijada.",
                equipment: "solo el cuerpo",
                primaryMuscles: ["cuádriceps", "isquiotibiales", "glúteos"],
                origin: Origin.Catalog,
                exerciseType: ExerciseType.Distance
            ));
        }

        // 3. Trote (Tiempo o Distancia)
        if (!existingNames.Contains("trote"))
        {
            exercisesToAdd.Add(Exercise.Create(
                name: "Trote",
                muscleGroup: "Piernas",
                description: "Carrera continua a ritmo aeróbico moderado.",
                equipment: "solo el cuerpo",
                primaryMuscles: ["cuádriceps", "gemelos"],
                origin: Origin.Catalog,
                exerciseType: ExerciseType.Time
            ));
        }

        // 4. Saltos (Repeticiones)
        if (!existingNames.Contains("saltos"))
        {
            exercisesToAdd.Add(Exercise.Create(
                name: "Saltos",
                muscleGroup: "Piernas",
                description: "Ejercicios pliométricos de rebote y potencia vertical u horizontal.",
                equipment: "solo el cuerpo",
                primaryMuscles: ["cuádriceps", "glúteos", "gemelos"],
                origin: Origin.Catalog,
                exerciseType: ExerciseType.Reps
            ));
        }

        if (exercisesToAdd.Count > 0)
        {
            await context.Exercises.AddRangeAsync(exercisesToAdd);
            await context.SaveChangesAsync();
            logger.LogInformation("KAN-453: Se agregaron {Count} ejercicios de carrera/resistencia al catálogo.", exercisesToAdd.Count);
        }
    }
}