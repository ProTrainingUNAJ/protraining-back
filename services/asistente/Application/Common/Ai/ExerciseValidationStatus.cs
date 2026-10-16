namespace Application.Common.Ai;

/// <summary>
/// Estado resultante tras la inspección semántica de un ejercicio individual propuesto por un LLM.
/// </summary>
public enum ExerciseValidationStatus
{
    /// <summary>
    /// El ejercicio posee un ejercicioId verificado y existente en el catálogo del club.
    /// </summary>
    Valid,

    /// <summary>
    /// El ejercicio tenía un ejercicioId inventado, inexistente o nulo, pero su nombre fue identificado
    /// en el catálogo y se corrigió asociándole el GUID real correspondiente.
    /// </summary>
    IdCorrected,

    /// <summary>
    /// El ejercicio no existe en el catálogo oficial; de acuerdo al requerimiento de ajuste (N2),
    /// se preserva con la bandera 'esReferencia = true' y 'ejercicioId = null' para revisión del preparador físico.
    /// </summary>
    BroughtAsReference,

    /// <summary>
    /// El ejercicio no existe en el catálogo oficial del club y no califica como referencia bajo la política de validación activa.
    /// </summary>
    Uncataloged
}
