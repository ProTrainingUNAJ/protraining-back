using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Seguimiento.Tests.Security;

// KAN-450: un atleta no puede ver datos de otro. Guardián: todo endpoint que recibe un id en la ruta
// tiene que ser solo para PF (o para servicios internos). Si alguien abre uno a cualquier rol sin
// validar que el id sea del atleta del token, este test falla.
[Trait("Category", "Isolation")]
public sealed class AthletePrivacyTests
{
    // Endpoints con id abiertos al atleta que validan que el id sea el suyo (y si no, responden 404).
    // CheckInsController.Submit: compara el id de la ruta con el atleta de /me y responde 404 si no es el suyo (KAN-431).
    // GroupBenchmarkController.GetGroupBenchmark: valida contra /me y responde 404 si no es su propio id (KAN-449 / KAN-450).
    private static readonly HashSet<string> AllowedForAthletes =
        ["CheckInsController.Submit", "DailyLoadsController.Register", "DailyLoadsController.Get", "DailyLoadsController.GetWeek", "GroupBenchmarkController.GetGroupBenchmark"];

    [Fact]
    public void Given_EveryEndpointWithIdInRoute_When_CheckingRoles_Then_NoneIsOpenToAthletes()
    {
        var endpoints = EndpointsWithIdInRoute().ToList();
        Assert.NotEmpty(endpoints); // que el guardián no pase "en vacío"

        var openEndpoints = endpoints
            .Where(endpoint => IsOpenToAthletes(endpoint.Roles) && !AllowedForAthletes.Contains(endpoint.Name))
            .Select(endpoint => $"{endpoint.Name} ({endpoint.Route})")
            .ToList();

        Assert.True(
            openEndpoints.Count == 0,
            "Endpoints con id en la ruta abiertos a atletas: " + string.Join(", ", openEndpoints)
            + ". Restringilos con [Authorize(Roles = \"PreparadorFisico\")] o validá que el id sea del atleta del token (404 si no).");
    }

    private static bool IsOpenToAthletes(string? roles) =>
        roles is null || roles.Split(',').Select(role => role.Trim()).Contains("Atleta");

    private static IEnumerable<(string Name, string Route, string? Roles)> EndpointsWithIdInRoute()
    {
        var controllers = typeof(API.Controllers.CheckIns.CheckInsController).Assembly.GetTypes()
            .Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract);

        foreach (var controller in controllers)
        {
            var controllerRoute = controller.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var controllerRoles = controller.GetCustomAttribute<AuthorizeAttribute>()?.Roles;

            foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                    continue;

                var roles = action.GetCustomAttribute<AuthorizeAttribute>()?.Roles ?? controllerRoles;

                // Una acción puede tener más de una ruta (por ejemplo, dos [HttpDelete]).
                foreach (var httpMethod in action.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var route = $"{controllerRoute}/{httpMethod.Template}".TrimEnd('/');
                    if (route.Contains('{'))
                        yield return ($"{controller.Name}.{action.Name}", route, roles);
                }
            }
        }
    }
}
