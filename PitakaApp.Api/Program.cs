using Microsoft.EntityFrameworkCore;
using PitakaApp.Api.Actions.Auth;
using PitakaApp.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PitakaApp.Api.Options;
using PitakaApp.Api.Services;
using System.Text.Json.Serialization;
using PitakaApp.Api.Actions;
using Microsoft.Extensions.Options;

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
builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<UpdateAccountBalance>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<BudgetService>();

builder.Services.AddOptions<JwtOption>()
    .Bind(builder.Configuration.GetSection(JwtOption.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Configured via IOptions<JwtOption> (resolved from DI, after the host is built) rather
// than a raw builder.Configuration read at top-level-statement time — a raw read here
// captures whatever config exists at that exact moment, which is too early to see
// configuration sources added later via WebApplicationFactory's ConfigureAppConfiguration
// (used by the test suite), even though it's always been early enough for real config
// sources (appsettings.json, User Secrets) which load synchronously inside CreateBuilder.
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOption>>((options, jwtOption) =>
    {
        var jwt = jwtOption.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
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
