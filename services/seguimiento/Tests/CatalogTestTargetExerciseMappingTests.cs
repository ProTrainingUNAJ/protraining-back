using System.Text.Json;
using Infrastructure.Data.Seeders;

namespace Seguimiento.Tests;

// KAN-580: el mapeo test → ejercicio de Entrenamiento es por nombre exacto
// ("Sentadilla 1RM" → "Sentadilla con barra") porque los ids no son estables entre
// entornos. Si alguien renombra ese ejercicio en exercises.json, la primera confirmación
// del 1RM en un entorno nuevo responde 400 y nadie se entera hasta que falla.
// Este test lee el exercises.json real de Entrenamiento y exige que cada
// TargetExerciseName del seeder exista ahí, con el nombre faltante a la vista.
public class CatalogTestTargetExerciseMappingTests
{
    [Fact]
    public void BuildTests_CadaTargetExerciseName_ExisteEnElCatalogoDeEntrenamiento()
    {
        var mappings = CatalogTestSeeder.BuildTests()
            .Where(test => !string.IsNullOrWhiteSpace(test.TargetExerciseName))
            .Select(test => (Test: test.Name, Target: test.TargetExerciseName!))
            .ToList();

        // Si el mapeo queda vacío el test no puede pasar "en blanco": alguien lo vació
        // y la confirmación del 1RM dejaría de tener objetivo.
        Assert.True(
            mappings.Count > 0,
            "CatalogTestSeeder no mapea ningún test a un ejercicio de Entrenamiento: " +
            "sin TargetExerciseName no hay récord 1RM que confirmar.");

        var faltantes = Faltantes(mappings, CatalogoDeEntrenamiento());

        Assert.True(
            faltantes.Count == 0,
            "Estos ejercicios del mapeo no existen en exercises.json de Entrenamiento " +
            "(fue renombrado o borrado el ejercicio): " + string.Join("; ", faltantes));
    }

    // El asertor del test de arriba no se puede pasar en blanco: con un nombre inventado
    // tiene que fallar nombrándolo (mismo criterio de aceptación, sin tocar datos reales).
    [Fact]
    public void Faltantes_NombreQueNoEstaEnElCatalogo_SeReportaConEseNombre()
    {
        var catalogo = new HashSet<string>(StringComparer.Ordinal) { "Sentadilla con barra" };

        var faltantes = Faltantes([("Sentadilla 1RM", "Sentadilla con barra borrada")], catalogo);

        Assert.Single(faltantes);
        Assert.Contains("Sentadilla con barra borrada", faltantes[0], StringComparison.Ordinal);
    }

    // Nombres exactos del catálogo de Entrenamiento, leídos del archivo del repo.
    private static HashSet<string> CatalogoDeEntrenamiento()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ExercisesJsonPath()));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exercise in document.RootElement.EnumerateArray())
            names.Add(exercise.GetProperty("name").GetString()!);

        return names;
    }

    private static List<string> Faltantes(
        IEnumerable<(string Test, string Target)> mappings,
        IReadOnlySet<string> catalog) =>
        mappings
            .Where(mapping => !catalog.Contains(mapping.Target))
            .Select(mapping => $"\"{mapping.Target}\" (test \"{mapping.Test}\")")
            .ToList();

    // Sube desde el binario de los tests hasta la raíz del checkout: los tests corren
    // desde bin/ y el archivo vive en otro microservicio (mismo caminar de carpetas que
    // TenantFilterGuardTests.FindServiceRoot). Si no está, el test falla: nunca pasa
    // en silencio.
    private static string ExercisesJsonPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName, "services", "entrenamiento", "API", "Data", "exercises.json");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "No se encontró services/entrenamiento/API/Data/exercises.json subiendo desde el " +
            "binario de los tests: el test tiene que correr desde el checkout del repositorio.");
    }
}
