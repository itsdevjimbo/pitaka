using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PitakaApp.Api.Options;

namespace PitakaApp.Api.Infra;

public static class JwtAuthenticationExtensions
{
    public static WebApplicationBuilder AddJwtAuthentication(this WebApplicationBuilder builder)
    {
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
        return builder;
    }
}