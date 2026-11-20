using System.Text.Json;
using System.Threading.RateLimiting;
using API.Extensions;
using API.HealthChecks;
using API.Middleware;
using API.Services;
using Application.Interfaces.Services;
using Application.UseCases.Auth.GoogleLogin;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.Refresh;
using Application.UseCases.Auth.UpdatePhone;
using Application.UseCases.Users.UpdateHabitsRecommendations;
using Application.UseCases.Users.GetPfHabitsRecommendations;
using Application.UseCases.Health.GetHealth;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Formatting.Compact;
using Application.UseCases.Auth.ChangePassword;
using Application.UseCases.Auth.GetPfContact;
using Infrastructure.Services.Structure;
using Application.UseCases.Auth.RegisterByInvitation;
using Application.UseCases.Auth.ResetAthletePassword;
using Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var serviceName = builder.Configuration["Service"] ?? "API";
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", serviceName)
    .WriteTo.Console(new CompactJsonFormatter()));
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                    entry => entry.Value!.Errors.Select(error =>
                        string.IsNullOrWhiteSpace(error.ErrorMessage)
                            ? "El valor no es válido. Comprueba el JSON y los tipos de los campos."
                            : error.ErrorMessage).ToArray());
            return new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Status = 400,
                Title = "Solicitud no válida."
            });
        };
    });

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFrontendCors(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);
// Retries, timeouts and circuit breaker for every registered HttpClient.
builder.Services.ConfigureHttpClientDefaults(http => http.AddProTrainingResilience());

// Rate Limiting para protección contra abuso en endpoints de autenticación y registro (#KAN-601)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Demasiadas solicitudes.",
            detail: "Has superado el límite de intentos permitidos. Por favor, intentá nuevamente en 15 minutos."
        ).ExecuteAsync(context.HttpContext);
    };

    options.AddPolicy("AuthRateLimit", httpContext =>
    {
        var clientIp = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: clientIp,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            });
    });
});

// Cliente HTTP hacia Estructura para validar invitaciones (KAN-397)
var structureBaseUrl = builder.Configuration["Services:Estructura:BaseUrl"] ?? "http://localhost:8082";
builder.Services.AddHttpClient<IStructureClient, StructureClient>(client =>
{
    client.BaseAddress = new Uri(structureBaseUrl);
});

builder.Services.AddScoped<GetHealthQueryHandler>();
builder.Services.AddScoped<LoginUseCase>();
builder.Services.AddScoped<GoogleLoginUseCase>();
builder.Services.AddScoped<LinkGoogleAccountUseCase>();
builder.Services.AddScoped<UnlinkGoogleAccountUseCase>();
builder.Services.AddScoped<GetGoogleLinkStatusUseCase>();
builder.Services.AddScoped<RefreshUseCase>();
builder.Services.AddScoped<UpdateWhatsAppPhoneUseCase>();
builder.Services.AddScoped<GetPfContactUseCase>();
builder.Services.AddScoped<UpdateHabitsRecommendationsUseCase>();
builder.Services.AddScoped<GetPfHabitsRecommendationsUseCase>();
builder.Services.AddScoped<RegisterByInvitationUseCase>();
builder.Services.AddScoped<Application.UseCases.Auth.RegisterPf.RegisterPfUseCase>();
builder.Services.AddScoped<Application.UseCases.Auth.ConfirmEmail.ConfirmEmailUseCase>();
builder.Services.AddScoped<Application.UseCases.Auth.ResendConfirmation.ResendConfirmationUseCase>();
builder.Services.AddScoped<ChangePasswordUseCase>();
builder.Services.AddScoped<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
builder.Services.AddScoped<ResetAthletePasswordUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.UpdateProfile.UpdateProfileUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.UpdateMyTelegram.UpdateMyTelegramUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.DeactivateAccount.DeactivateAccountUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.UpdateAthleteUserStatus.UpdateAthleteUserStatusUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.UpdateAthleteUserEmail.UpdateAthleteUserEmailUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.UpdateAthleteUserPhone.UpdateAthleteUserPhoneUseCase>();
builder.Services.AddScoped<Application.UseCases.Users.PurgeAthlete.PurgeAthleteCommandHandler>();

builder.Services.AddScoped<Application.UseCases.Users.UploadUserPhoto.UploadUserPhotoCommandHandler>();
builder.Services.AddScoped<Application.UseCases.Users.GetUserPhoto.GetUserPhotoQueryHandler>();
builder.Services.AddScoped<Application.UseCases.Users.GetPfPhoto.GetPfPhotoQueryHandler>();
builder.Services.AddScoped<Application.UseCases.Users.DeleteUserPhoto.DeleteUserPhotoCommandHandler>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerWithBearer(serviceName);
}

var app = builder.Build();
await app.Services.ApplyMigrationsAsync();

// solo en dev creamos roles y el usuario inicial de prueba
if (app.Environment.IsDevelopment())
{
    await Infrastructure.Data.IdentityDataSeed.SeedAsync(app.Services);
}


app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

