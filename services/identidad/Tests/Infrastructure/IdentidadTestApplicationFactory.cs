using System.Text.Json;
using API.Extensions;
using API.Middleware;
using API.Services;
using Application.Common.Security;
using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.UseCases.Auth.GetPfContact;
using Application.UseCases.Auth.Login;
using Application.UseCases.Auth.Refresh;
using Application.UseCases.Auth.RegisterByInvitation;
using Application.UseCases.Auth.UpdatePhone;
using Application.UseCases.Health.GetHealth;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Services;
using Infrastructure.Services.Structure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Identidad.Tests.Infrastructure;

public sealed class IdentidadTestApplicationFactory : IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _sqliteConnection;
    private readonly TestServer _server;

    public FakeStructureHandler Structure { get; }

    public IdentidadTestApplicationFactory()
    {
        Structure = new FakeStructureHandler();
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "clave_local_de_desarrollo_cambiar_32+",
                ["Jwt:Issuer"] = "protraining-identidad",
                ["Jwt:Audience"] = "protraining",
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
                ["Services:Estructura:BaseUrl"] = "http://fake-estructura/"
            })
            .Build();

        var builder = new WebHostBuilder()
            .UseEnvironment("Development")
            .UseConfiguration(configuration)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);

                // SQLite en memoria para aislar los tests de la base real
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_sqliteConnection));

                // Identity Core
                services.AddIdentityCore<User>(options =>
                {
                    options.Password.RequireDigit = true;
                    options.Password.RequiredLength = 6;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireUppercase = false;
                    options.Password.RequireLowercase = false;
                    options.User.RequireUniqueEmail = true;
                })
                .AddRoles<IdentityRole<Guid>>()
                .AddEntityFrameworkStores<AppDbContext>()
                .AddDefaultTokenProviders();

                // Repositorios y Servicios
                services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
                services.AddScoped<IJwtTokenGenerator, JwtTokenGenerator>();
                services.AddScoped<IUnitOfWork, UnitOfWork>();
                services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
                services.AddHttpContextAccessor();
                services.AddScoped<ICurrentUser, CurrentUser>();

                // Inyección de HttpClient hacia Estructura mockeado con FakeStructureHandler
                services.AddHttpClient<IStructureClient, StructureClient>(client =>
                {
                    client.BaseAddress = new Uri("http://fake-estructura/");
                })
                .ConfigurePrimaryHttpMessageHandler(() => Structure);

                // UseCases
                services.AddScoped<GetHealthQueryHandler>();
                services.AddScoped<LoginUseCase>();
                services.AddScoped<RefreshUseCase>();
                services.AddScoped<UpdateWhatsAppPhoneUseCase>();
                services.AddScoped<GetPfContactUseCase>();
                services.AddScoped<RegisterByInvitationUseCase>();

                services.AddFrontendCors(configuration);
                services.AddJwtAuthentication(configuration);

                services.AddControllers()
                    .AddApplicationPart(typeof(API.Controllers.AuthController).Assembly)
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
                                            ? "El valor no es válido."
                                            : error.ErrorMessage).ToArray());
                            return new BadRequestObjectResult(new ValidationProblemDetails(errors)
                            {
                                Status = 400,
                                Title = "Solicitud no válida."
                            });
                        };
                    });
            })
            .Configure(app =>
            {
                app.UseMiddleware<CorrelationIdMiddleware>();
                app.UseMiddleware<ExceptionMiddleware>();

                app.UseRouting();
                app.UseCors(ServiceCollectionExtensions.CorsPolicyName);
                app.UseAuthentication();
                app.UseAuthorization();

                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });
            });

        _server = new TestServer(builder);

        // Crear tablas en memoria y sembrar roles básicos
        using var scope = _server.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "PreparadorFisico", "Atleta", "Tutor" })
        {
            if (!roleManager.RoleExistsAsync(role).GetAwaiter().GetResult())
            {
                roleManager.CreateAsync(new IdentityRole<Guid> { Name = role }).GetAwaiter().GetResult();
            }
        }
    }

    public HttpClient CreateClient() => _server.CreateClient();

    public async Task<User?> FindUserByEmailAsync(string email)
    {
        using var scope = _server.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return await userManager.FindByEmailAsync(email);
    }

    public async Task<IList<string>> GetUserRolesAsync(User user)
    {
        using var scope = _server.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return await userManager.GetRolesAsync(user);
    }

    public async Task<User> SeedUserAsync(string email, string fullName, string role, string password = "PasswordSegura123!")
    {
        using var scope = _server.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            UserName = email,
            Email = email,
            FullName = fullName,
            EmailConfirmed = true,
            IsActive = true
        };
        await userManager.CreateAsync(user, password);
        await userManager.AddToRoleAsync(user, role);
        return user;
    }

    public async ValueTask DisposeAsync()
    {
        _server.Dispose();
        await _sqliteConnection.DisposeAsync();
    }

    public void Dispose()
    {
        _server.Dispose();
        _sqliteConnection.Dispose();
    }
}