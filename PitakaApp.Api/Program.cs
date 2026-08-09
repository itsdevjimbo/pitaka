using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
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

builder.Services.AddScoped<RegisterUser>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program class to PitakaApp.Api.Tests, since
// WebApplicationFactory<Program> needs it to be accessible from another assembly.
public partial class Program { }
