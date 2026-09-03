using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Data;
using System.Text.Json.Serialization;
using PitakaApp.Api.Infra;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using PitakaApp.Api.Handlers;
using PitakaApp.Api.ModelBinding;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers(options =>
    {
        options.Conventions.Add(new RouteTokenTransformerConvention(new SlugifyParameterTransformer()));

        // A DateTimeOffset bound from the wire (query/route/form, never a JSON body) must
        // carry its own zone designator. Without this the default binder reads a bare
        // timestamp in the server's zone — a different answer in the container than on a
        // developer's machine. Governs `from`/`to` on GET /api/transactions. See ADR 0005.
        options.ModelBinderProviders.Insert(0, new ZoneBearingDateTimeOffsetModelBinderProvider());
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
builder.AddEmailSender();
builder.AddPitakaCors();
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

// Before UseHttpsRedirection deliberately (Microsoft's docs order it the other way) so a
// plain-HTTP preflight is answered rather than 307'd. Reasoning: ADR 0002 and the slice 1 spec.
app.UseCors(CorsExtensions.PolicyName);

// Only outside Development. An unguarded redirect 307s the client's plain-HTTP requests
// (the SDK loop's `https` profile has a live TLS port) and strips their CORS headers.
// Environment is the whole decision; no host here terminates TLS. Refines ADR 0002.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the top-level Program class to PitakaApp.Api.Tests, since
// WebApplicationFactory<Program> needs it to be accessible from another assembly.
public partial class Program { }
