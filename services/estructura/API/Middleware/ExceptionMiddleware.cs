using Application.Exceptions;
using Domain.Exceptions;

namespace API.Middleware;

public sealed class ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El cliente cerró la solicitud; no intentar enviar otra respuesta.
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
                throw;

            var (status, title, detail) = exception switch
            {
                DomainException domain => (400, domain.Message, (string?)null),
                AthleteNotFoundException => (404, "Atleta no encontrado.", null),
                PersistenceConcurrencyException => (409, "Conflicto de concurrencia.",
                    "El atleta cambió durante la operación. Recarga e inténtalo de nuevo."),
                PersistenceUnavailableException => (503, "Base de datos no disponible.",
                    "Comprueba el estado de la base de datos y su configuración. Inténtalo de nuevo."),
                BadHttpRequestException badRequest => (badRequest.StatusCode, "Solicitud no válida.",
                    "Comprueba el JSON, los tipos de los campos y Content-Type: application/json."),
                _ => (500, "Error interno del servidor.", null)
            };

            if (status >= 500)
                logger.LogError("Solicitud fallida. Estado {Status}; tipo {Type}.", status, exception.GetType().Name);

            await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
        }
    }
}
