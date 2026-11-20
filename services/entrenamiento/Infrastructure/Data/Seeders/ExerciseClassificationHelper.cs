namespace Infrastructure.Data.Seeders;

// Reglas de negocio para la clasificación de ejercicios del catálogo (KAN-590).
// Estandariza los grupos musculares oficiales: Piernas, Pecho, Espalda, Hombros, Brazos y Core.
public static class ExerciseClassificationHelper
{
    // Devuelve el grupo muscular obligatorio para ejercicios básicos según las reglas de KAN-590,
    // o null si el ejercicio no requiere una regla específica y debe usar su músculo anatómico.
    public static string? DetermineBasicMuscleGroup(string name, string? sourceId = null)
    {
        var n = name.ToLowerInvariant();
        var id = (sourceId ?? string.Empty).ToLowerInvariant();

        // 1. Pesos muertos y todas sus variantes (convencional, rumano, con eje, sumo, con déficit, hexagonal, etc.) -> Piernas (cadena posterior)
        if (n.Contains("peso muerto") || id.Contains("deadlift"))
        {
            return "Piernas";
        }

        // 2. Hip thrust y puente de glúteo -> Piernas
        if (n.Contains("hip thrust") || n.Contains("empuje de cadera") || n.Contains("puente de glúteo") || n.Contains("puente de gluteo") || id.Contains("hip_thrust") || id.Contains("glute_bridge"))
        {
            return "Piernas";
        }

        // Movilidad de columna (Gato-camello) -> Core
        if (n.Contains("gato-camello") || id.Contains("cat_stretch"))
        {
            return "Core";
        }

        // 3. Sentadillas, prensa de piernas y resistencia/carrera básica -> Piernas
        if ((n.Contains("sentadilla") || n.Contains("prensa de piernas") || id.Contains("squat") || id.Contains("leg_press")) && !id.Contains("turkish"))
        {
            return "Piernas";
        }

        if (n == "trote" || n == "sprints" || n == "pasadas" || n == "saltos")
        {
            return "Piernas";
        }

        // 4. Press militar y empujes verticales de hombro (push press, overhead press) -> Hombros
        if (n.Contains("press militar") || n.Contains("prensa militar") || id.Contains("military_press") || id.Contains("overhead_press") || id.Contains("push_press") || id.Contains("seated_press"))
        {
            return "Hombros";
        }

        // 5. Press de banca y fondos -> Pecho
        if (n.Contains("press de banca") || id.Contains("bench_press") || id.Contains("floor_press") || n.Contains("prensa de piso") || n.Contains("prensa de suelo"))
        {
            return "Pecho";
        }

        if (n.Contains("fondos") || id.StartsWith("dip") || id.Contains("dip_machine") || id == "bench_dips")
        {
            return "Pecho";
        }

        // 6. Dominadas y jalones -> Espalda
        if (n.Contains("dominada") || id.Contains("pull-up") || id.Contains("chin-up"))
        {
            return "Espalda";
        }

        if (n.Contains("jalón") || n.Contains("jalon") || id.Contains("pulldown"))
        {
            return "Espalda";
        }

        // 7. Remos de espalda (excluyendo remo vertical / al cuello que trabaja deltoides/trapecios) -> Espalda
        if (n.Contains("remo con") || n.Contains("remo en") || n.Contains("remo inclinado") || n.Contains("remo largo") || n.Contains("remo renegade") || id.Contains("barbell_rear_delt_row") || id.Contains("cable_rope_rear-delt_rows"))
        {
            if (n.Contains("vertical") || id.Contains("upright") || n.Contains("cuello"))
            {
                return "Hombros";
            }

            return "Espalda";
        }

        return null;
    }

    public static string DetermineMuscleGroup(string name, string? sourceId = null, string? fallbackMuscle = null)
    {
        var basicGroup = DetermineBasicMuscleGroup(name, sourceId);
        if (basicGroup != null)
        {
            return basicGroup;
        }

        // Fallback a mapeo por músculo anatómico primario
        if (!string.IsNullOrWhiteSpace(fallbackMuscle))
        {
            return ExerciseCatalogSeeder.NormalizeMuscleGroup(fallbackMuscle);
        }

        return "Cuerpo Completo";
    }
}
