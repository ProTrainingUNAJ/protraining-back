using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Estructura.Tests.Security;

// KAN-450: un atleta no puede ver datos de otro. Guardián: todo endpoint que recibe un id en la ruta
// tiene que ser solo para PF (o para servicios internos). Si alguien abre uno a cualquier rol sin
// validar que el id sea del atleta del token, este test falla.
[Trait("Category", "Isolation")]
public sealed class AthletePrivacyTests
{
    // Endpoints con id abiertos al atleta que validan que el id sea el suyo (y si no, responden 404).
    private static readonly HashSet<string> AllowedForAthletes =
    [
        "CategoryEventsController.List",
        "CategorySeasonPhasesController.List"
    ];

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
        var controllers = typeof(API.Controllers.MeController).Assembly.GetTypes()
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
