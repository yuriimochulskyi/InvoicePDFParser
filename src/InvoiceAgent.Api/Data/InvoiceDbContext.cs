using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace InvoiceAgent.Api.Data;

public sealed class InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : DbContext(options)
{
    public DbSet<InvoiceRecord> Invoices => Set<InvoiceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var invoice = modelBuilder.Entity<InvoiceRecord>();
        invoice.Property(x => x.Status).HasConversion<string>();
        // SQLite has no native DateTimeOffset ordering; store as ISO text.
        invoice.Property(x => x.CreatedAt).HasConversion(v => v.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            v => DateTimeOffset.Parse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }
}
