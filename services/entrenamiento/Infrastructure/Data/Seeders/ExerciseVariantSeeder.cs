using Domain.Entities;
using Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Data.Seeders;

// Agrupa las variantes del catálogo en su ejercicio base (KAN-554), según
// Data/exercise-variants.json. Busca los ejercicios por nombre exacto, así sirve en una base
// que ya tiene el catálogo cargado (los ids no cambian). Corre en cada arranque y no repite
// cambios: si el JSON se amplía, la próxima vez aplica solo lo nuevo.
public class ExerciseVariantSeeder
{
    private const string FileName = "exercise-variants.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task SeedAsync(AppDbContext context, string dataDirectoryPath, ILogger logger)
    {
        var jsonFilePath = Path.Combine(dataDirectoryPath, FileName);
        if (!File.Exists(jsonFilePath))
        {
            logger.LogWarning("No se encontró el archivo de variantes en {Path}", jsonFilePath);
            return;
        }

        var families = JsonSerializer.Deserialize<List<VariantFamily>>(
            await File.ReadAllTextAsync(jsonFilePath), JsonOptions) ?? [];

        // Solo el catálogo global (PfId vacío), que es el que agrupan las familias.
        var catalog = await context.Exercises
            .IgnoreQueryFilters()
            .Where(e => e.PfId == Guid.Empty && e.DeletedAt == null)
            .ToListAsync();

        var byName = catalog
            .GroupBy(e => e.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Id).ToList(), StringComparer.Ordinal);

        var parentsWithVariants = catalog
            .Where(e => e.ParentExerciseId.HasValue)
            .Select(e => e.ParentExerciseId!.Value)
            .ToHashSet();

        var changes = 0;

        foreach (var family in families)
        {
            if (string.IsNullOrWhiteSpace(family.Base) || !byName.TryGetValue(family.Base.Trim(), out var baseMatches))
            {
                logger.LogWarning("Variantes: no se encontró el ejercicio base {Base}; se saltea la familia.", family.Base);
                continue;
            }

            var baseExercise = baseMatches[0];
            if (baseMatches.Count > 1)
            {
                logger.LogWarning("Variantes: el ejercicio base {Base} está {Count} veces en el catálogo; se usa el primero.",
                    family.Base, baseMatches.Count);
            }

            if (baseExercise.IsVariant)
            {
                logger.LogWarning("Variantes: {Base} ya es variante de otro ejercicio; se saltea la familia.", family.Base);
                continue;
            }

            foreach (var variant in family.Variants ?? [])
            {
                if (string.IsNullOrWhiteSpace(variant.Name) || !byName.TryGetValue(variant.Name.Trim(), out var variantMatches))
                {
                    logger.LogWarning("Variantes: no se encontró la variante {Variant} de {Base}.", variant.Name, family.Base);
                    continue;
                }

                // Si el nombre está repetido en el catálogo, todas las filas pasan a ser la misma variante.
                foreach (var exercise in variantMatches)
                {
                    if (exercise.Id == baseExercise.Id)
                        continue;

                    var label = variant.Label?.Trim() ?? string.Empty;
                    if (exercise.ParentExerciseId == baseExercise.Id && exercise.VariantLabel == label)
                        continue;

                    try
                    {
                        exercise.AssignAsVariantOf(baseExercise, label, parentsWithVariants.Contains(exercise.Id));
                        changes++;
                    }
                    catch (DomainException ex)
                    {
                        logger.LogWarning("Variantes: no se pudo asignar {Variant} a {Base}: {Reason}",
                            variant.Name, family.Base, ex.Message);
                    }
                }
            }

            parentsWithVariants.Add(baseExercise.Id);
        }

        if (changes > 0)
        {
            await context.SaveChangesAsync();
            logger.LogInformation("Variantes: se agruparon {Count} ejercicios en sus ejercicios base.", changes);
        }
    }

    private sealed class VariantFamily
    {
        public string Base { get; set; } = string.Empty;
        public List<VariantItem>? Variants { get; set; }
    }

    private sealed class VariantItem
    {
        public string Name { get; set; } = string.Empty;
        public string? Label { get; set; }
    }
}
