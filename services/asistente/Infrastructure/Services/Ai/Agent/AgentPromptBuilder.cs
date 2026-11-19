using System.Text;
using Application.DTOs.DayAdjustments;
using Application.DTOs.PlanDrafts;

namespace Infrastructure.Services.Ai.Agent;

/// <summary>
/// Constructor de prompts especializados para el agente de IA metodológico de ProTraining.
/// Garantiza anonimización estricta (cero PII enviada a modelos externos) y especificación
/// del protocolo de invocación a herramientas MCP de entrenamiento.
/// </summary>
public static class AgentPromptBuilder
{
    public const string SystemPrompt = """
        Sos el agente metodológico de entrenamiento deportivo de alto rendimiento de ProTraining.
        Tu rol es diseñar planificaciones deportivas individualizadas y adaptaciones metodológicas rigurosas para preparadores físicos (PFs).

        REGLAS OPERATIVAS OBLIGATORIAS:
        1. HERRAMIENTAS MCP:
           - Tenés a tu disposición herramientas MCP conectadas a la base oficial de ejercicios de ProTraining.
           - Antes de armar la planificación, utilizá la herramienta 'BuscarEjercicios' filtrando por músculo, equipamiento o nombre para obtener ejercicios válidos con sus IDs reales (GUID).
           - Si existen zonas de dolor o lesión reportadas, utilizá 'ObtenerRestricciones' y 'ObtenerSustitutos' para asegurar ejercicios biomecánicamente seguros.
           - Nunca inventes GUIDs de ejercicios; utilizá los IDs devueltos por las herramientas.

        2. METODOLOGÍA DEPORTIVA:
           - Diseñá las sesiones modelo de la primera semana respetando la secuencia funcional:
             a) Entrada en calor / movilidad / activación.
             b) Bloque principal de fuerza/potencia.
             c) Bloque secundario o neuromuscular.
             d) Accesorios / preventivos / compensatorios.
           - Asigná series (entre 1 y 10), repeticiones (entre 1 y 30) y porcentajes de carga coherentes (entre 40% y 95%).
           - El motor determinístico en C# de ProTraining se encargará de proyectar y progresar las semanas posteriores.

        3. PRIVACIDAD Y SEGURIDAD:
           - No se incluyen nombres, apellidos, emails ni datos de contacto de ningún atleta.

        4. FORMATO DE SALIDA ESTRICTO (JSON):
           - Cuando tengas toda la información necesaria de las herramientas, devolvé ÚNICAMENTE un objeto JSON con la siguiente estructura:
           {
             "name": "Nombre descriptivo de la planificación",
             "objective": "Objetivo técnico-físico del plan",
             "justificationSummary": "Resumen metodológico explicando la selección de ejercicios y progresión",
             "weeks": [
               {
                 "weekNumber": 1,
                 "name": "Semana 1 - Fase de Adaptación / Carga",
                 "sessions": [
                   {
                     "name": "Sesión 1 - Fuerza Torso / Tren Superior",
                     "dayOfWeek": "Monday",
                     "order": 1,
                     "coachNotes": "Indicaciones para el preparador físico",
                     "exercises": [
                       {
                         "exerciseId": "GUID_DEL_CATALOGO",
                         "exerciseName": "Nombre oficial devuelto por la tool",
                         "order": 1,
                         "sets": 4,
                         "reps": 6,
                         "targetPercentage": 80.0,
                         "coachNotes": "Controlar velocidad excéntrica"
                       }
                     ]
                   }
                 ]
               }
             ]
           }
        """;

    public static string BuildPlanDraftUserPrompt(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SOLICITUD DE PLANIFICACIÓN DEPORTIVA:");
        sb.AppendLine($"- Objetivo principal: {request.Goal}");
        sb.AppendLine($"- Duración requerida: {request.Weeks} semanas");
        sb.AppendLine($"- Frecuencia semanal: {request.DaysPerWeek} días por semana");

        if (request.StartDate.HasValue)
        {
            sb.AppendLine($"- Fecha de inicio: {request.StartDate.Value:yyyy-MM-dd}");
        }

        if (matchDates is { Count: > 0 })
        {
            sb.AppendLine($"- Fechas de partidos competitivos programados: {string.Join(", ", matchDates.Select(d => d.ToString("yyyy-MM-dd")))}");
            sb.AppendLine("  (Recordá reducir carga y volumen 24-48 horas previas a cada partido)");
        }

        sb.AppendLine();
        sb.AppendLine("Instrucción: Consultá las herramientas MCP para obtener ejercicios adecuados para este objetivo y devolvé el borrador en JSON.");
        return sb.ToString();
    }

    public static string BuildDayAdjustmentPrompt(IReadOnlyList<DayAdjustmentItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Sos el agente metodológico de ProTraining. Redactá una justificación técnica breve (2 o 3 oraciones), clara y profesional en español rioplatense (tratamiento de 'vos') dirigida al preparador físico, explicando por qué se realizó el ajuste de la sesión de hoy:");
        sb.AppendLine();

        foreach (var item in items)
        {
            sb.AppendLine($"- Ejercicio original: {item.OriginalExerciseName}");
            if (!string.IsNullOrWhiteSpace(item.SubstituteExerciseName))
            {
                sb.AppendLine($"  Sustituido por: {item.SubstituteExerciseName}");
            }
            else if (!string.IsNullOrWhiteSpace(item.SuggestedSubstituteExerciseName))
            {
                sb.AppendLine($"  Sustituto sugerido: {item.SuggestedSubstituteExerciseName}");
            }

            if (!string.IsNullOrWhiteSpace(item.Zone))
            {
                sb.AppendLine($"  Zona afectada: {item.Zone} (Nivel de molestia: {item.PainLevel})");
            }

            if (!string.IsNullOrWhiteSpace(item.Reason))
            {
                sb.AppendLine($"  Motivo: {item.Reason}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Devolvé solo el texto de la justificación, sin etiquetas ni explicaciones adicionales.");
        return sb.ToString();
    }
}
