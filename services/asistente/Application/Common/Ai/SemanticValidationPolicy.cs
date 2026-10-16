namespace Application.Common.Ai;

/// <summary>
/// Políticas que controlan el rigor de la validación semántica y las condiciones de conmutación
/// hacia el motor determinístico de respaldo.
/// </summary>
public sealed record SemanticValidationPolicy
{
    /// <summary>
    /// Política estándar por defecto: Permite ejercicios de referencia (Ajuste N2) y conmuta
    /// al motor determinístico si ningún ejercicio del catálogo es reconocido.
    /// </summary>
    public static SemanticValidationPolicy Default => new();

    /// <summary>
    /// Política estricta: Requiere que todos los ejercicios existan en el catálogo.
    /// Si alguno no existe, conmuta inmediatamente a generación determinística segura.
    /// </summary>
    public static SemanticValidationPolicy Strict => new()
    {
        AllowReferenceExercises = false,
        MaxAllowedUncataloged = 0,
        RequireAtLeastOneCatalogExercise = true
    };

    /// <summary>
    /// Si es true, permite que los ejercicios no encontrados en el catálogo se incorporen como
    /// ejercicios de referencia ('esReferencia = true', 'ejercicioId = null'), de acuerdo al ajuste de la tarjeta #049.
    /// Si es false, cualquier ejercicio fuera del catálogo provocará conmutación a determinístico.
    /// </summary>
    public bool AllowReferenceExercises { get; init; } = true;

    /// <summary>
    /// Número máximo de ejercicios no identificados permitidos antes de conmutar al motor determinístico.
    /// Por defecto 1 (si más de 1 ejercicio no es reconocido, conmuta al motor determinístico).
    /// </summary>
    public int MaxAllowedUncataloged { get; init; } = 1;

    /// <summary>
    /// Propiedad de compatibilidad.
    /// </summary>
    public int MaxAllowedHallucinations
    {
        get => MaxAllowedUncataloged;
        init => MaxAllowedUncataloged = value;
    }

    /// <summary>
    /// Si es true, la rutina debe contener al menos un ejercicio legítimo y validado contra el catálogo.
    /// Si ningún ejercicio propuesto pertenece al catálogo, conmuta automáticamente a determinístico.
    /// </summary>
    public bool RequireAtLeastOneCatalogExercise { get; init; } = true;
}
