using System.Text.Json;
using System.Text.RegularExpressions;
using Infrastructure.Data.Seeders;

namespace Seguimiento.Tests;

// KAN-580: el mapeo test → ejercicio de Entrenamiento es por nombre exacto
// ("Sentadilla 1RM" → "Sentadilla con barra") porque los ids no son estables entre
// entornos. Si alguien renombra o borra ese ejercicio, la primera confirmación
// del 1RM en un entorno nuevo responde 400 y nadie se entera hasta que falla.
// El catálogo real de Entrenamiento tiene dos fuentes en el repo: exercises.json
// (ExerciseCatalogSeeder) y los ejercicios que EnduranceExerciseSeeder siembra en
// código (KAN-453: "Pasadas", "Sprints", "Trote", "Saltos"), que el JSON no trae.
// Este test exige que cada TargetExerciseName exista en alguna de las dos,
// con el nombre faltante a la vista.
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
            "Estos ejercicios del mapeo no existen en el catálogo de Entrenamiento " +
            "(ni en exercises.json ni en EnduranceExerciseSeeder; fue renombrado o " +
            "borrado el ejercicio): " + string.Join("; ", faltantes));
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

    // Catálogo real de Entrenamiento en un entorno nuevo: exercises.json (876) más
    // los ejercicios que EnduranceExerciseSeeder siembra en código al arrancar.
    // Solo el JSON no alcanza: "Pasadas" (KAN-572) vive en el seeder y el lookup en
    // runtime es por nombre contra la base, no contra el archivo.
    private static HashSet<string> CatalogoDeEntrenamiento()
    {
        var names = NombresDelJson();
        names.UnionWith(NombresDelEnduranceSeeder());

        return names;
    }

    // Nombres exactos del catálogo de Entrenamiento, leídos del exercises.json del repo.
    private static HashSet<string> NombresDelJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ExercisesJsonPath()));

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exercise in document.RootElement.EnumerateArray())
            names.Add(exercise.GetProperty("name").GetString()!);

        return names;
    }

    // Ejercicios que EnduranceExerciseSeeder (KAN-453) siembra en código, leídos del
    // archivo fuente: son los name: "..." de sus Exercise.Create. Si el archivo no está
    // o el regex no encuentra ningún nombre (cambió la forma de declararlos), el test
    // falla acá en vez de cruzar los brazos con un catálogo incompleto.
    private static HashSet<string> NombresDelEnduranceSeeder()
    {
        var source = File.ReadAllText(EnduranceSeederPath());

        var names = new HashSet<string>(
            Regex.Matches(source, @"name:\s*""([^""]+)""")
                .Select(match => match.Groups[1].Value),
            StringComparer.Ordinal);

        Assert.True(
            names.Count > 0,
            "EnduranceExerciseSeeder.cs no expone ningún name: \"...\": revisá que el " +
            "seeder siga declarando los ejercicios con Exercise.Create(name: ...) — " +
            "si cambió, actualizá este test antes de darlo por bueno.");

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
    // desde bin/ y los archivos viven en otro microservicio (mismo caminar de carpetas que
    // TenantFilterGuardTests.FindServiceRoot). Si no está, el test falla: nunca pasa
    // en silencio.
    private static string ExercisesJsonPath() =>
        RepoFile("services", "entrenamiento", "API", "Data", "exercises.json");

    private static string EnduranceSeederPath() =>
        RepoFile(
            "services", "entrenamiento", "Infrastructure", "Data", "Seeders",
            "EnduranceExerciseSeeder.cs");

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "No se encontró " + Path.Combine(parts) + " subiendo desde el " +
            "binario de los tests: el test tiene que correr desde el checkout del repositorio.");
    }
}
