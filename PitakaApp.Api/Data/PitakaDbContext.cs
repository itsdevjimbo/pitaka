using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Data;

// IdentityUserContext<User, int>, not IdentityDbContext<...> — that adds role, user-role
// and role-claim tables for a role model the app has no use for.
// IDataProtectionKeyContext backs AddDataProtection().PersistKeysToDbContext<PitakaDbContext>()
// (see IdentityExtensions), persisting the Data Protection key ring here instead of the
// per-machine default that a container redeploy wipes.
public class PitakaDbContext : IdentityUserContext<User, int>, IDataProtectionKeyContext
{
    public PitakaDbContext(DbContextOptions<PitakaDbContext> options) : base(options)
    {
    }

    public DbSet<Account> Accounts { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<RecurringTransaction> RecurringTransactions { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<Goal> Goals { get; set; }
    public DbSet<GoalContribution> GoalContributions { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity's mappings (users, user_claims, user_logins, user_tokens and their
        // indexes) must be in place before the project's configuration below runs on
        // top of them.
        base.OnModelCreating(modelBuilder);

        // House style: no asp_net_* names anywhere. IdentityUserContext maps
        // AspNetUsers/AspNetUserClaims/AspNetUserLogins/AspNetUserTokens by default.
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("user_tokens");

        // Identity maps EmailIndex on normalized_email as non-unique; email uniqueness
        // would otherwise ride on the UserName mirror (UserNameIndex is the only unique
        // one). Make the email index unique in its own right so the database is the
        // backstop for the change-email request-then-redeem race (ADR 0014). The
        // Identity-default name "EmailIndex" is kept — renaming an index Identity already
        // created buys nothing — so this stays PascalCase like its sibling UserNameIndex
        // rather than the project's ix_* convention.
        modelBuilder.Entity<User>()
            .HasIndex(u => u.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique();

        modelBuilder.Entity<Account>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Category>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Budget>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Goal>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<RecurringTransaction>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Tag>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        var enumProperties = modelBuilder.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType).IsEnum)
            .ToList();

        // convert all enums to string and sets column type to varchar
        foreach (var property in enumProperties)
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            var converterType = typeof(EnumToStringConverter<>).MakeGenericType(enumType);
            var converter = (ValueConverter)Activator.CreateInstance(converterType, (object?)null)!;
            property.SetValueConverter(converter);
            property.SetColumnType("varchar(100)");
        }

        modelBuilder.Entity<Account>()
            .Property(a => a.Version)
            .IsConcurrencyToken();

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.TransferToAccount)
            .WithMany()
            .HasForeignKey(t => t.TransferToAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Category>()
            .HasOne(c => c.User)
            .WithMany(u => u.Categories)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Budget>()
            .HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(rt => rt.Category)
            .WithMany()
            .HasForeignKey(rt => rt.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not SetNull: a generated transaction's RecurringTransactionId is the
        // discriminator other rules read (ADR 0007, ADR 0005, #71), so deleting a schedule
        // must not null it out from under them. A schedule with generated transactions is
        // in use and cannot be deleted; the person cancels it instead (ADR 0008).
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.RecurringTransaction)
            .WithMany()
            .HasForeignKey(t => t.RecurringTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GoalContribution>()
            .HasOne(gc => gc.Transaction)
            .WithOne(t => t.GoalContribution)
            .HasForeignKey<GoalContribution>(gc => gc.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GoalContribution>()
            .HasOne(gc => gc.Account)
            .WithMany()
            .HasForeignKey(gc => gc.AccountId);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        foreach (var entry in ChangeTracker.Entries<ITimestamped>()
             .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<Account>()
             .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.Version += 1;
        }

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ITimestamped>()
             .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.UpdatedAt = DateTime.UtcNow;
        }

        foreach (var entry in ChangeTracker.Entries<Account>()
             .Where(e => e.State == EntityState.Modified))
        {
            entry.Entity.Version += 1;
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
