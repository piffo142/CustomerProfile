using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SIG.ClientCard.Core.Entities;
using SIG.ClientCard.Data.Entities;

namespace SIG.ClientCard.Data;

public class ClientCardContext(DbContextOptions<ClientCardContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientNote> ClientNotes => Set<ClientNote>();
    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();
    public DbSet<ClientConsent> ClientConsents => Set<ClientConsent>();
    public DbSet<Salon> Salons => Set<Salon>();
    public DbSet<SyncOutboxEntry> Outbox => Set<SyncOutboxEntry>();
    public DbSet<SyncDeadLetterEntry> DeadLetters => Set<SyncDeadLetterEntry>();
    public DbSet<SyncStateRow> SyncState => Set<SyncStateRow>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Guids as canonical lowercase TEXT — not BLOB, because Guid.ToByteArray()
        // uses mixed-endian layout and will not match Postgres byte ordering when
        // diffed outside the app.
        configurationBuilder.Properties<Guid>().HaveConversion<GuidToLowerStringConverter>();
        configurationBuilder.Properties<Guid?>().HaveConversion<NullableGuidToLowerStringConverter>();

        // SQLite cannot order or compare DateTimeOffset TEXT columns; store UTC
        // ticks as INTEGER so ORDER BY and range predicates translate.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<NullableDateTimeOffsetToUtcTicksConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>(e =>
        {
            e.ToTable("client");
            e.HasKey(x => x.Id);
            e.Property(x => x.LastName).IsRequired();
            e.Property(x => x.FirstName).IsRequired();
            e.HasIndex(x => new { x.LastName, x.FirstName });
            e.HasIndex(x => new { x.SalonId, x.SyncSeq });
            e.Ignore(x => x.DisplayName);

            e.HasMany(x => x.Notes).WithOne().HasForeignKey(n => n.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Services).WithOne().HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Consents).WithOne().HasForeignKey(c => c.ClientId).OnDelete(DeleteBehavior.Cascade);

        });

        modelBuilder.Entity<ClientNote>(e =>
        {
            e.ToTable("client_note");
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).IsRequired();
            e.HasIndex(x => new { x.ClientId, x.CreatedAt });
            e.HasIndex(x => new { x.SalonId, x.SyncSeq });
        });

        modelBuilder.Entity<ServiceRecord>(e =>
        {
            e.ToTable("service_record");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceDescription).IsRequired();
            e.Property(x => x.Price).HasPrecision(10, 2);
            e.Property(x => x.CurrencyCode).HasMaxLength(3);
            e.HasIndex(x => new { x.ClientId, x.PerformedOn });
            e.HasIndex(x => new { x.SalonId, x.SyncSeq });
        });

        modelBuilder.Entity<ClientConsent>(e =>
        {
            e.ToTable("client_consent");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ClientId, x.Purpose });
            e.HasIndex(x => new { x.SalonId, x.SyncSeq });
        });

        modelBuilder.Entity<Salon>(e =>
        {
            e.ToTable("salon");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<SyncOutboxEntry>(e =>
        {
            e.ToTable("sync_outbox");
            e.HasKey(x => x.OpId);
            e.Property(x => x.Entity).IsRequired();
            e.Property(x => x.Operation).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_outbox_created");
        });

        modelBuilder.Entity<SyncDeadLetterEntry>(e =>
        {
            e.ToTable("sync_dead_letter");
            e.HasKey(x => x.OpId);
        });

        modelBuilder.Entity<SyncStateRow>(e =>
        {
            e.ToTable("sync_state");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });

        // snake_case column names everywhere so local rows, outbox payloads and
        // the Postgres schema all share one vocabulary.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

public sealed class GuidToLowerStringConverter() : ValueConverter<Guid, string>(
    g => g.ToString("D"),
    s => Guid.Parse(s));

public sealed class NullableGuidToLowerStringConverter() : ValueConverter<Guid?, string?>(
    g => g == null ? null : g.Value.ToString("D"),
    s => s == null ? null : Guid.Parse(s));

public sealed class DateTimeOffsetToUtcTicksConverter() : ValueConverter<DateTimeOffset, long>(
    d => d.UtcTicks,
    t => new DateTimeOffset(t, TimeSpan.Zero));

public sealed class NullableDateTimeOffsetToUtcTicksConverter() : ValueConverter<DateTimeOffset?, long?>(
    d => d == null ? null : d.Value.UtcTicks,
    t => t == null ? null : new DateTimeOffset(t.Value, TimeSpan.Zero));
