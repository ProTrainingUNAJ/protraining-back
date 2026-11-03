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
                InvalidCredentialsException invalidCreds => (401, invalidCreds.Message, (string?)null),
                InvalidRefreshTokenException invalidRefresh => (401, invalidRefresh.Message, (string?)null),
                PfRoleForbiddenException pfForbidden => (403, "Acceso no permitido.", pfForbidden.Message),
                AthleteAlreadyExistsException athleteExists => (409, athleteExists.Message, (string?)null),
                InvitationExpiredException expired => (410, "Invitación expirada.", expired.Message),
                InvitationNotFoundException notFound => (400, "Invitación inválida.", notFound.Message),
                StructureUnavailableException unavailable => (503, "Servicio no disponible.", unavailable.Message),
                DomainException domain => (400, domain.Message, (string?)null),
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