using Domain.Entities;

namespace Application.Interfaces.Services;

/// <summary>
/// contrato para el generador de tokens JWT autofirmados
/// </summary>
public interface IJwtTokenGenerator
{
    /// <summary>
    /// genera un token JWT con los claims requeridos por la plataforma (sub, email, role, pf_id)
    /// </summary>
    /// <param name="user">entidad del usuario autenticado.</param>
    /// <param name="role">rol principal del usuario (PreparadorFisico o Atleta).</param>
    /// <returns>El string del token JWT firmado.</returns>
    string GenerateToken(Usuario user, string role);
}
