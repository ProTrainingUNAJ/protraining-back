using Infrastructure.Data.Seeders;

namespace Entrenamiento.Tests;

public class ExerciseCatalogSeederTests
{
    [Theory]
    // Pecho
    [InlineData("chest", "Pecho")]
    [InlineData("pecho", "Pecho")]
    // Espalda
    [InlineData("lats", "Espalda")]
    [InlineData("middle back", "Espalda")]
    [InlineData("espalda media", "Espalda")]
    [InlineData("lower back", "Espalda")]
    [InlineData("espalda baja", "Espalda")]
    [InlineData("traps", "Espalda")]
    [InlineData("trapecios", "Espalda")]
    // Piernas
    [InlineData("quadriceps", "Piernas")]
    [InlineData("cuadríceps", "Piernas")]
    [InlineData("hamstrings", "Piernas")]
    [InlineData("isquiotibiales", "Piernas")]
    [InlineData("calves", "Piernas")]
    [InlineData("gemelos", "Piernas")]
    [InlineData("glutes", "Piernas")]
    [InlineData("glúteos", "Piernas")]
    [InlineData("adductors", "Piernas")]
    [InlineData("aductores", "Piernas")]
    [InlineData("abductors", "Piernas")]
    [InlineData("abductores", "Piernas")]
    // Hombros
    [InlineData("shoulders", "Hombros")]
    [InlineData("hombros", "Hombros")]
    [InlineData("neck", "Hombros")]
    [InlineData("cuello", "Hombros")]
    // Brazos
    [InlineData("biceps", "Brazos")]
    [InlineData("bíceps", "Brazos")]
    [InlineData("triceps", "Brazos")]
    [InlineData("tríceps", "Brazos")]
    [InlineData("forearms", "Brazos")]
    [InlineData("antebrazos", "Brazos")]
    // Core
    [InlineData("abdominals", "Core")]
    [InlineData("abdominales", "Core")]
    // Desconocido / Default
    [InlineData("desconocido", "Cuerpo Completo")]
    public void DadoMusculoDelDataset_CuandoNormaliza_EntoncesRetornaGrupoCorrecto(string inputMuscle, string expectedGroup)
    {
        var result = ExerciseCatalogSeeder.NormalizeMuscleGroup(inputMuscle);
        Assert.Equal(expectedGroup, result);
    }
}
