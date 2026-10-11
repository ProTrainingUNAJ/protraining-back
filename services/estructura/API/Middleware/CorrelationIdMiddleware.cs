using Serilog.Context;

namespace API.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (!IsValid(correlationId))
            correlationId = Guid.NewGuid().ToString();

        context.TraceIdentifier = correlationId!;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // The client's id is accepted only if it is short and has no odd characters,
    // so nothing harmful can be injected into the logs.
    private static bool IsValid(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
}
