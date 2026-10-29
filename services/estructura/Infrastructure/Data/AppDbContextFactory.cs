using Application.Interfaces.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Data;

// Used only by "dotnet ef" to create migrations, without starting the API or requiring environment variables.
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost")
            .Options;

        return new AppDbContext(options, new NoUser());
    }

    private sealed class NoUser : ICurrentUser
    {
        public Guid? PfId => null;
        public Guid? UserId => null;
        public string? Role => null;
    }
}
