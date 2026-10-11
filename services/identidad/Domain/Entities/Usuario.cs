using Microsoft.AspNetCore.Identity;

namespace Domain.Entities;

public class Usuario : IdentityUser<Guid>
{
    public string NombreCompleto { get; set; } = string.Empty;
    public Guid? PfId { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? ActualizadoEn { get; set; }

    public Usuario()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString();
    }
}
