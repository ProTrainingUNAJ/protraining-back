namespace Application.DTOs.Exercise;

// Ilustración o animación de un ejercicio del catálogo (KAN-549). Vienen ordenadas por preferencia:
// el front muestra la primera que cargue y, si ninguna carga, las fotos de Images.
// Kind "frames": imágenes fijas que el front alterna (posición inicial y final) para que se vea el movimiento.
// Kind "gif": una animación que ya se mueve sola.
public sealed record ExerciseMediaDto(
    string Source,
    string Kind,
    IReadOnlyList<string> Urls,
    string Credit,
    string CreditUrl,
    string License);
