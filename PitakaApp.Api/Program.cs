using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using System.Text.Json.Serialization;
using PitakaApp.Api.Infra;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
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

builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program class to PitakaApp.Api.Tests, since
// WebApplicationFactory<Program> needs it to be accessible from another assembly.
public partial class Program { }
