using ProTraining.Common;

namespace ProTraining.WebAPI.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El cliente abortó la petición HTTP
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
                throw;

            var (status, title, detail) = ex switch
            {
                DomainException domain => (400, "Error de Validación de Negocio", domain.Message),
                NotFoundException notFound => (404, "Recurso no Encontrado", notFound.Message),
                ConcurrencyException concurrency => (409, "Conflicto de Concurrencia", concurrency.Message),
                _ => (500, "Error Interno del Servidor", "Ocurrió un error inesperado al procesar la solicitud.")
            };

            if (status >= 500)
            {
                _logger.LogError(ex, "Error no controlado al procesar la solicitud: {Message}", ex.Message);
            }

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";

            var problem = new
            {
                type = $"https://httpstatuses.com/{status}",
                title,
                status,
                detail,
                instance = context.Request.Path
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}
