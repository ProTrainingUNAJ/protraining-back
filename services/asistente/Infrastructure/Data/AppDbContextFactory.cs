using Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Data;

// Lo usa sólo "dotnet ef" para crear migraciones, sin levantar la API ni pedir variables de entorno.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;

        return new AppDbContext(options, new SinUsuario());
    }

    private sealed class SinUsuario : IUsuarioActual
    {
        public Guid? PfId => null;
    }
}
