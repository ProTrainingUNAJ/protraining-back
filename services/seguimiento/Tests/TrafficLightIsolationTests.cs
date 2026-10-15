using System.Net.Http;
using System.Reflection;
using Application.UseCases.Health.GetHealth;
using Domain.Entities;
using Domain.Services;
using Xunit;

namespace Seguimiento.Tests;

// KAN-364 — Guardas de aislamiento del semáforo.
// Regla de negocio: el semáforo se calcula solo con datos locales (check-ins de la base de Seguimiento)
// y funciona aunque el Asistente u otros servicios estén caídos. Estos tests hacen cumplir esa regla:
// si alguien introduce una llamada HTTP en el path del semáforo, acá fallan.
public class TrafficLightIsolationTests
{
    // Asambleas del dominio y de la aplicación: son las que calculan el semáforo y jamás deben hablar por HTTP.
    private static readonly Assembly DomainAssembly = typeof(CheckIn).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(GetHealthQueryHandler).Assembly;

    // El cliente HTTP, si algún día hace falta (llamada opcional al Asistente), solo puede vivir
    // en Infrastructure detrás de una interfaz con plan B local: nunca en Domain ni en Application.
    private static readonly Assembly[] AssembliesDelSemaforo = [DomainAssembly, ApplicationAssembly];

    [Fact]
    public void Domain_NoDeberiaReferenciarHttpClientNiPaquetesDeResiliencia()
    {
        var referencias = DomainAssembly.GetReferencedAssemblies().Select(assembla => assembla.Name).ToList();

        Assert.DoesNotContain("System.Net.Http", referencias);
        Assert.DoesNotContain("Microsoft.Extensions.Http", referencias);
        Assert.DoesNotContain("Polly", referencias);
    }

    [Fact]
    public void Application_NoDeberiaReferenciarHttpClientNiPaquetesDeResiliencia()
    {
        var referencias = ApplicationAssembly.GetReferencedAssemblies().Select(assembla => assembla.Name).ToList();

        Assert.DoesNotContain("System.Net.Http", referencias);
        Assert.DoesNotContain("Microsoft.Extensions.Http", referencias);
        Assert.DoesNotContain("Polly", referencias);
    }

    [Fact]
    public void CasosDeUsoYEntidades_NoDeberianInyectarHttpClientNiIHttpClientFactory()
    {
        var infracciones = new List<string>();

        foreach (var assembly in AssembliesDelSemaforo)
        {
            foreach (var tipo in assembly.GetExportedTypes())
            {
                var dependenciasDeHttp = tipo
                    .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .SelectMany(ctor => ctor.GetParameters())
                    .Select(parametro => parametro.ParameterType)
                    .Concat(tipo.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Select(campo => campo.FieldType))
                    .Where(dependencia => typeof(HttpClient).IsAssignableFrom(dependencia)
                                   || typeof(IHttpClientFactory).IsAssignableFrom(dependencia))
                    .Select(dependencia => dependencia.Name);

                foreach (var dependencia in dependenciasDeHttp)
                    infracciones.Add($"{tipo.FullName} → {dependencia}");
            }
        }

        Assert.True(infracciones.Count == 0,
            "El cálculo del semáforo no puede inyectar HTTP: las llamadas, si algún día existen, van " +
            $"detrás de una interfaz en Infrastructure con plan B local. Violaciones: {string.Join("; ", infracciones)}");
    }

    [Fact]
    public void TrafficLightEngine_NoDeberiaTenerDependenciasInyectadasNiBaseDeDatos()
    {
        // Motor puro: sin constructor parametrizado no puede recibir DbContext, HttpClient ni ningún servicio.
        var constructores = typeof(TrafficLightEngine).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Single(constructores);
        Assert.Empty(constructores[0].GetParameters());

        var camposDeInstancia = typeof(TrafficLightEngine)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (var campo in camposDeInstancia)
        {
            Assert.False(typeof(HttpClient).IsAssignableFrom(campo.FieldType),
                $"TrafficLightEngine no debe guardar un {campo.FieldType.Name}.");
            Assert.False(campo.FieldType.FullName?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true,
                "TrafficLightEngine no debe depender de EF Core: recibe los datos ya cargados de la tabla local de check-ins.");
        }
    }

    [Fact]
    public void TrafficLightEngine_DeberiaCalcularElSemaforoSinNingunaFuenteExterna()
    {
        // Prueba de fuego de la autonomía: una instancia recién creada, sin nada inyectado,
        // debe poder clasificar al atleta. Así funciona el semáforo con el Asistente caído.
        var motor = new TrafficLightEngine();

        var result = motor.Evaluate("""{"hasInjury": true, "sleepHours": 8}""", "{}");

        Assert.Equal(TrafficLight.Red, result.TriggeredLight);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }
}
