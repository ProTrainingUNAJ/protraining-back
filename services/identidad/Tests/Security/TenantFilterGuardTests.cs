using System.Text.RegularExpressions;
using Application.Interfaces.Services;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Identidad.Tests.Security;

// KAN-432: suite de aislamiento entre PFs. Si alguien agrega una tabla sin el filtro por PF o una
// consulta que lo apaga, el CI falla en el paso "Aislamiento entre PFs" y el PR no se puede mergear.
[Trait("Category", "Isolation")]
public sealed class TenantFilterGuardTests
{
    // Tablas sin filtro por PF a propósito, con el motivo.
    private static readonly Dictionary<string, string> EntitiesWithoutPfFilter = new()
    {
        ["User"] = "Cuenta de login: se busca por email antes de saber el PF del usuario.",
        ["RefreshToken"] = "Se valida al renovar la sesión, antes de saber el PF.",
        ["IdentityRole`1"] = "Tabla de ASP.NET Identity (roles), compartida por todos los PFs.",
        ["IdentityRoleClaim`1"] = "Tabla de ASP.NET Identity.",
        ["IdentityUserClaim`1"] = "Tabla de ASP.NET Identity.",
        ["IdentityUserLogin`1"] = "Tabla de ASP.NET Identity.",
        ["IdentityUserRole`1"] = "Tabla de ASP.NET Identity.",
        ["IdentityUserToken`1"] = "Tabla de ASP.NET Identity.",
    };

    // Archivos que pueden usar IgnoreQueryFilters(), con el motivo.
    private static readonly Dictionary<string, string> FilesAllowedToIgnoreFilters = new();

    // Toda entidad del modelo tiene el filtro global por PF (hereda de BaseEntity), salvo las de la lista.
    [Fact]
    public void Given_EveryEntityInTheModel_When_CheckingQueryFilters_Then_AllAreFilteredByPf()
    {
        using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options,
            new Mock<ICurrentUser>().Object);

        var entityTypes = context.Model.GetEntityTypes()
            .Where(entityType => entityType.BaseType is null && !entityType.IsOwned())
            .ToList();
        Assert.NotEmpty(entityTypes); // que el guardián no pase "en vacío"

        var withoutFilter = entityTypes
            .Where(entityType => entityType.GetQueryFilter() is null && !EntitiesWithoutPfFilter.ContainsKey(entityType.ClrType.Name))
            .Select(entityType => entityType.ClrType.Name)
            .ToList();

        Assert.True(
            withoutFilter.Count == 0,
            "Tablas sin filtro por PF: " + string.Join(", ", withoutFilter)
            + ". Hacé que la entidad herede de BaseEntity (le agrega pf_id y el filtro) o, si es un catálogo compartido, sumala a la lista con el motivo.");
    }

    // Nadie apaga el filtro por PF (IgnoreQueryFilters) en el código de producción, salvo los archivos de la lista.
    [Fact]
    public void Given_ProductionCode_When_SearchingIgnoreQueryFilters_Then_OnlyAllowedFilesUseIt()
    {
        var serviceRoot = FindServiceRoot();
        var offending = new[] { "API", "Application", "Domain", "Infrastructure" }
            .Select(folder => Path.Combine(serviceRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\.IgnoreQueryFilters\s*\("))
            .Select(file => Path.GetFileName(file))
            .Where(fileName => !FilesAllowedToIgnoreFilters.ContainsKey(fileName))
            .ToList();

        Assert.True(
            offending.Count == 0,
            "Archivos que apagan el filtro por PF (IgnoreQueryFilters): " + string.Join(", ", offending)
            + ". Si de verdad hace falta (por ejemplo, un proceso en segundo plano sin PF), sumalo a la lista con el motivo.");
    }

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
        || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    // Carpeta del servicio (la que tiene Infrastructure y Tests), subiendo desde donde corren los tests.
    private static string FindServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !(Directory.Exists(Path.Combine(directory.FullName, "Infrastructure"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Tests"))))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("No se encontró la carpeta del servicio.");
    }
}
