using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PitakaApp.Api.Models;

namespace PitakaApp.Api.Data;

public class PitakaDbContext : DbContext
{
    public PitakaDbContext(DbContextOptions<PitakaDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Account> Accounts { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<RecurringTransaction> RecurringTransactions { get; set; }
    public DbSet<Budget> Budgets { get; set; }
    public DbSet<Goal> Goals { get; set; }
    public DbSet<GoalContribution> GoalContributions { get; set; }
    public DbSet<Tag> Tags { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().HasIndex(u => u.Email).IsUnique();

        modelBuilder.Entity<Account>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Category>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Budget>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Goal>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<RecurringTransaction>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<Tag>().HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        modelBuilder.Entity<PasswordResetToken>().HasIndex(t => t.TokenHash).IsUnique();

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
            .HasOne(c => c.Parent)
            .WithMany(c => c.Children)
            .HasForeignKey(c => c.ParentId)
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
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(rt => rt.Category)
            .WithMany()
            .HasForeignKey(rt => rt.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany()
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.RecurringTransaction)
            .WithMany()
            .HasForeignKey(t => t.RecurringTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<GoalContribution>()
            .HasOne(gc => gc.Transaction)
            .WithOne(t => t.GoalContribution)
            .HasForeignKey<GoalContribution>(gc => gc.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<GoalContribution>()
            .HasOne(gc => gc.Account)
            .WithMany()
            .HasForeignKey(gc => gc.AccountId);

        // Cascade like Category → User: deleting a user takes its reset tokens with it.
        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        foreach (var entry in ChangeTracker.Entries<TimestampedEntity>()
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
        foreach (var entry in ChangeTracker.Entries<TimestampedEntity>()
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
