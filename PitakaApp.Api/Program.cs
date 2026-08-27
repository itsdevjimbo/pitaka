using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using System.Text.Json.Serialization;
using PitakaApp.Api.Infra;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using PitakaApp.Api.Handlers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options =>
    {
        options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));
    })
    .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        }
    );
builder.Services.AddOpenApi();
builder.Services.AddDbContext<PitakaDbContext>((serviceProvider, options) =>
    {
        var connectionString = serviceProvider.GetRequiredService<IConfiguration>()
            .GetConnectionString("DefaultConnection");
        options
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .UseSnakeCaseNamingConvention()
            .UseSeeding((context, _) => DbSeeder.Seed(context))
            .UseAsyncSeeding(async (context, _, cancellationToken) => await DbSeeder.SeedAsync(context, cancellationToken));
    });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApplicationServices();
builder.AddJwtAuthentication();
builder.AddRecurringTransactionGeneration();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program class to PitakaApp.Api.Tests, since
// WebApplicationFactory<Program> needs it to be accessible from another assembly.
public partial class Program { }
