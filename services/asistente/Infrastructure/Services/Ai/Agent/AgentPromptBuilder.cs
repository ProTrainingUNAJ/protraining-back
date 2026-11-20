using System.Text;
using Application.Common.Ai;
using Application.DTOs.Ai;
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
           - Para ejercicios oficiales del catálogo, utilizá siempre los IDs (GUID) devueltos por las herramientas.
           - Si el preparador físico solicita explícitamente ejercicios que no existen en el catálogo tras buscarlos con la herramienta (ej. Curl Nórdico, Plancha Copenhagen, etc.), asignales "exerciseId": null en el JSON y su "exerciseName" solicitado. ProTraining los creará automáticamente como ejercicios de referencia nuevos (isReference).
           - Nunca inventes GUIDs falsos de ejercicios; si no existe en el catálogo, poné "exerciseId": null.

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

        4. CONTEXTO DEL PEDIDO:
           - Si el pedido incluye restricciones por lesión o dolor, no propongas ejercicios restringidos y usá únicamente los sustitutos autorizados que figuren en el pedido.
           - Si incluye decisiones previas del preparador físico, usalas como evidencia de su criterio, sin tratarlas como reglas absolutas si contradicen las restricciones actuales.

        5. FORMATO DE SALIDA ESTRICTO (JSON):
           - Si el pedido trae un CATÁLOGO OFICIAL numerado y un esquema JSON por bloques (warmUp, strengthA, strengthB, accessories), respondé ÚNICAMENTE con un JSON que cumpla ese esquema: usá el "exerciseIndex" de la lista y diseñá solo la sesión modelo de la primera semana.
           - Solo si el pedido no trae ese catálogo numerado, devolvé ÚNICAMENTE un objeto JSON con la siguiente estructura semanal:
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
                         "exerciseId": "GUID_DEL_CATALOGO (o null si es nuevo)",
                         "exerciseName": "Nombre oficial o solicitado",
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

    /// <summary>
    /// Arma el pedido de planificación para el agente (KAN-600). Incluye el destinatario (categoría o atleta, solo por
    /// identificador anónimo), las restricciones por lesión, las decisiones previas del PF, las directivas deportivas y,
    /// si hay catálogo, el formato por bloques con la lista numerada (KAN-589 / KAN-591 / KAN-468).
    /// </summary>
    public static string BuildPlanDraftUserPrompt(
        CreatePlanDraftRequestDto request,
        IReadOnlyList<DateOnly>? matchDates = null,
        IReadOnlyList<CatalogExerciseDto>? catalog = null)
    {
        // El objetivo es texto libre del PF: antes de salir a un proveedor externo se quitan emails y teléfonos
        var safeRequest = request with { Goal = PromptSanitizer.RedactContactData(request.Goal) };

        var sb = new StringBuilder();
        sb.AppendLine("SOLICITUD DE PLANIFICACIÓN DEPORTIVA:");
        AppendRecipient(sb, safeRequest);

        if (catalog is { Count: > 0 })
        {
            sb.AppendLine($"- Duración requerida: {safeRequest.Weeks} semanas (diseñá solo la sesión modelo de la semana 1: las demás las proyecta ProTraining).");
            sb.AppendLine("Podés consultar las herramientas MCP para verificar ejercicios, restricciones y sustitutos, pero la respuesta final debe usar el catálogo numerado de abajo.");
            sb.AppendLine();
            sb.Append(RoutinePromptBuilder.BuildBlockPlanDraftPrompt(safeRequest, catalog, matchDates));
            return sb.ToString();
        }

        sb.AppendLine($"- Objetivo principal: {safeRequest.Goal}");
        sb.AppendLine($"- Duración requerida: {safeRequest.Weeks} semanas");
        sb.AppendLine($"- Frecuencia semanal: {safeRequest.DaysPerWeek} días por semana");

        if (safeRequest.StartDate.HasValue)
        {
            sb.AppendLine($"- Fecha de inicio: {safeRequest.StartDate.Value:yyyy-MM-dd}");
        }

        if (matchDates is { Count: > 0 })
        {
            sb.AppendLine($"- Fechas de partidos competitivos programados: {string.Join(", ", matchDates.Select(d => d.ToString("yyyy-MM-dd")))}");
            sb.AppendLine("  (Recordá reducir carga y volumen 24-48 horas previas a cada partido)");
        }

        sb.AppendLine();
        RoutinePromptBuilder.AppendPfDecisionContext(sb, safeRequest.PreviousPfDecisions);
        RoutinePromptBuilder.AppendAdaptationContext(sb, safeRequest.AdaptationContext);

        sb.AppendLine("Instrucción: Consultá las herramientas MCP para obtener ejercicios adecuados para este objetivo y devolvé el borrador en JSON.");
        return sb.ToString();
    }

    private static void AppendRecipient(StringBuilder sb, CreatePlanDraftRequestDto request)
    {
        if (request.CategoryId is { } categoryId && categoryId != Guid.Empty)
        {
            sb.AppendLine($"- Destinatario: categoría completa (identificador anónimo {categoryId}).");
        }
        else if (request.AthleteId is { } athleteId && athleteId != Guid.Empty)
        {
            sb.AppendLine($"- Destinatario: atleta individual (identificador anónimo {athleteId}).");
        }
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
