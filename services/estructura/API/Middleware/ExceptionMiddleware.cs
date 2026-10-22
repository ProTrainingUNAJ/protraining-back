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
            // The client closed the request; do not try to send another response.
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
                throw;

            var (status, title, detail) = exception switch
            {
                DomainException domain => (400, domain.Message, (string?)null),
                AthleteNotFoundException => (404, "Atleta no encontrado.", null),
                OrganizationNotFoundException => (404, "Organización no encontrada.", null),
                CategoryNotFoundException => (404, "Categoría no encontrada.", null),
                ConflictException conflict => (409, conflict.Message, (string?)null),
                PersistenceConcurrencyException => (409, "Conflicto de concurrencia.",
                    "El dato cambió durante la operación. Recarga e inténtalo de nuevo."),
                PersistenceUnavailableException => (503, "Base de datos no disponible.",
                    "Comprueba el estado de la base de datos y su configuración. Inténtalo de nuevo."),
                BadHttpRequestException badRequest => (badRequest.StatusCode, "Solicitud no válida.",
                    "Comprueba el JSON, los tipos de los campos y Content-Type: application/json."),
                UnauthorizedAccessException => (401, "No autenticado.",
                    "Falta el token o no tiene un PF asignado."),
                _ => (500, "Error interno del servidor.", null)
            };

            if (status >= 500)
                logger.LogError("Solicitud fallida. Estado {Status}; tipo {Type}.", status, exception.GetType().Name);

            await Results.Problem(statusCode: status, title: title, detail: detail).ExecuteAsync(context);
        }
    }
}
