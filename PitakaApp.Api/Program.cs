using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
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
builder.Services.AddScoped<LoginUser>();
builder.Services.AddScoped<GenerateJwtToken>();
builder.Services.AddScoped<GetCurrentUser>();
builder.Services.AddScoped<CategoryService>();

builder.Services.AddOptions<JwtOption>()
    .Bind(builder.Configuration.GetSection(JwtOption.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var jwtOptionJwtOption = builder.Configuration.GetSection(JwtOption.SectionName).Get<JwtOption>()
    ?? throw new InvalidOperationException($"Missing '{JwtOption.SectionName}' configuration section.");
    
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptionJwtOption.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptionJwtOption.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptionJwtOption.Key)),
            ValidateLifetime = true,
        };
    });

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
