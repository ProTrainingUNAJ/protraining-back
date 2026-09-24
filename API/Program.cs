using System.Text.Json;
using API.Middleware;
using Application.UseCases.Athletes.CreateAthlete;
using Application.UseCases.Athletes.DeleteAthlete;
using Application.UseCases.Athletes.GetAthlete;
using Application.UseCases.Athletes.ListAthletes;
using Application.UseCases.Athletes.UpdateAthleteCategory;
using Application.UseCases.Health.GetHealth;
using Infrastructure;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
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
builder.Services.AddScoped<ListAthletesQueryHandler>();
builder.Services.AddScoped<GetAthleteQueryHandler>();
builder.Services.AddScoped<CreateAthleteCommandHandler>();
builder.Services.AddScoped<UpdateAthleteCategoryCommandHandler>();
builder.Services.AddScoped<DeleteAthleteCommandHandler>();
builder.Services.AddScoped<GetHealthQueryHandler>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}

var app = builder.Build();
app.UseMiddleware<ExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
