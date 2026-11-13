namespace Application.Interfaces.Services;

/// <summary>
/// Generador de tokens JWT con rol 'Servicio' para llamar a endpoints internos de otros microservicios (KAN-578).
/// </summary>
public interface IServiceTokenFactory
{
    string CreateToken();
}

