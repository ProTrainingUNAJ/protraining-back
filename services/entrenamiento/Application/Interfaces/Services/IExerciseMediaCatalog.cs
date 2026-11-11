using Application.DTOs.Exercise;

namespace Application.Interfaces.Services;

// Ilustraciones y animaciones de terceros para los ejercicios del catálogo global (KAN-549).
// La tabla de cruce es fija (API/Data/exercise-media.json): se lee una vez al arrancar.
public interface IExerciseMediaCatalog
{
    // Recibe las imágenes del ejercicio del catálogo (las URLs de free-exercise-db), porque de ahí
    // sale su id original. Devuelve la lista vacía si el ejercicio no tiene ilustraciones.
    IReadOnlyList<ExerciseMediaDto> GetForCatalogImages(IReadOnlyList<string> catalogImages);
}
