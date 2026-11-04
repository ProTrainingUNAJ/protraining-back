namespace Application.Interfaces.Services;

/// <summary>
/// Generador de tokens JWT para comunicaciones entre microservicios (llamadas de workers en segundo plano) (#177 · KAN-472).
/// </summary>
public interface IServiceTokenFactory
{
    string CreateToken();
}

