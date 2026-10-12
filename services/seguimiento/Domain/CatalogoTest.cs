namespace Domain.Common;

public sealed class CatalogoTest : BaseEntity
{
    public string Nombre { get; private set; }
    public string CapacidadMotora { get; private set; }
    public string UnidadMedida { get; private set; }
    public int NivelDificultad { get; private set; }
    public bool Activo { get; private set; }

    private CatalogoTest() { }

    public CatalogoTest(string nombre, string capacidadMotora, string unidadMedida, int nivelDificultad = 1, bool activo = true)
    {
        Nombre = nombre;
        CapacidadMotora = capacidadMotora;
        UnidadMedida = unidadMedida;
        NivelDificultad = nivelDificultad;
        Activo = activo;
    }

    public void Actualizar(string nombre, string capacidadMotora, string unidadMedida, int nivelDificultad, bool activo)
    {
        Nombre = nombre;
        CapacidadMotora = capacidadMotora;
        UnidadMedida = unidadMedida;
        NivelDificultad = nivelDificultad;
        Activo = activo;
    }

    public void Desactivar() => Activo = false;
}