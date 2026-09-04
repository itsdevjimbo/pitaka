using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PitakaApp.Api.Data;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Infra;

// A hand-rolled IXmlRepository backed by PitakaDbContext, standing in for
// Microsoft.AspNetCore.DataProtection.EntityFrameworkCore's PersistKeysToDbContext.
// That package ships only at the same major version as the ASP.NET Core shared
// framework it targets; Data Protection's own abstractions (IXmlRepository,
// IDataProtectionBuilder) are part of that shared framework and resolve fine against
// this app's net10.0 target, but the EntityFrameworkCore package is pinned to EF Core
// 10, which this project cannot take while EFCore.NamingConventions and Pomelo stay on
// EF Core 9. This repository reads/writes the same one table (see DataProtectionKey)
// that the official package would have used, so swapping to it later — once the EF
// Core stack moves to 10 — is a like-for-like replacement, not a migration.
//
// Data Protection resolves its IXmlRepository outside any per-request scope, so this
// creates its own DbContext scope per call rather than taking one by constructor
// injection.
public class EfDataProtectionKeyRepository : IXmlRepository
{
    private readonly IServiceProvider _services;

    public EfDataProtectionKeyRepository(IServiceProvider services)
    {
        _services = services;
    }

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();

        return context.DataProtectionKeys
            .AsNoTracking()
            .Select(k => k.Xml)
            .ToList()
            .Where(xml => xml != null)
            .Select(xml => XElement.Parse(xml!))
            .ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var scope = _services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PitakaDbContext>();

        context.DataProtectionKeys.Add(new DataProtectionKey
        {
            FriendlyName = friendlyName,
            Xml = element.ToString(SaveOptions.DisableFormatting),
        });
        context.SaveChanges();
    }
}
