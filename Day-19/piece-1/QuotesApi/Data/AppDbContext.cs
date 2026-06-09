using Microsoft.EntityFrameworkCore;
using QuotesApi.Messaging;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote>            Quotes            => Set<Quote>();
    public DbSet<Collection>       Collections       => Set<Collection>();
    public DbSet<User>             Users             => Set<User>();
    public DbSet<RefreshToken>     RefreshTokens     => Set<RefreshToken>();
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.HasKey(q => q.Id);
            entity.Property(q => q.Author).IsRequired().HasMaxLength(200);
            entity.Property(q => q.Text).IsRequired().HasMaxLength(1000);
            entity.Property(q => q.IsDeleted).HasDefaultValue(false);
            entity.Property(q => q.OwnerId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
            entity.Property(u => u.Role).IsRequired().HasMaxLength(20).HasDefaultValue("viewer");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Token).IsRequired().HasMaxLength(128);
            entity.Property(r => r.FamilyId).IsRequired();
            entity.Property(r => r.RevokedAt);
            entity.Property(r => r.ReplacedByToken).HasMaxLength(128);
            entity.HasIndex(r => r.Token).IsUnique();
            entity.HasIndex(r => r.FamilyId);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(c => c.Id);
            
            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.OwnsMany(c => c.Items, items =>
            {
                // Day-11: explicit table name so SQL Server execution-plan capture and
                // sys.dm_db_missing_index_details refer to a stable, well-known target.
                // Without this, EF Core's default naming convention varies by provider.
                items.ToTable("CollectionItems");
                items.WithOwner().HasForeignKey("CollectionId");
                items.Property(i => i.QuoteId).IsRequired().ValueGeneratedNever();
                items.Property(i => i.AddedAt).IsRequired();
                items.HasKey("CollectionId", "QuoteId"); // Composite key for the owned type

                // Day-11 Piece-2: nonclustered index on QuoteId (the join column).
                // sys.dm_db_missing_index_details recommended this after Piece-1's k6
                // load baseline.  On a fresh DB (EnsureCreated) it is created here;
                // on the existing QuotesApiPerf DB apply it via sql/fix-add-index.sql.
                //
                // Without the index, the JOIN side that filters items by QuoteId
                // (or scans CollectionItems for any QuoteId-based predicate) falls
                // back to a Clustered Index Scan over the (CollectionId, QuoteId)
                // composite PK — fine for tiny tables, expensive when load grows.
                // With the index, that scan becomes an Index Seek.
                items.HasIndex("QuoteId").HasDatabaseName("IX_CollectionItems_QuoteId");
            });
            
            // Allow EF to set the private backing field
            entity.Metadata.FindNavigation(nameof(Collection.Items))
                ?.SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        // Day-19: idempotency table for Service Bus message deduplication.
        // The UNIQUE index on (MessageId, SubscriptionName) is the concurrency guard:
        // if two competing consumers race to record the same message, the second
        // SaveChanges throws DbUpdateException which the handler catches and treats
        // as "already processed".
        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.MessageId).IsRequired().HasMaxLength(128);
            entity.Property(p => p.SubscriptionName).IsRequired().HasMaxLength(100);
            entity.Property(p => p.ProcessedAt).IsRequired();
            entity.HasIndex(p => new { p.MessageId, p.SubscriptionName })
                  .IsUnique()
                  .HasDatabaseName("UQ_ProcessedMessages_MessageId_Subscription");
        });
    }
}