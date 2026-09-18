using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Scoring;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="AuditLogEntry"/> to <c>AuditLog</c>.</summary>
public sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("AuditLog");
        builder.HasKey(entry => entry.Id);

        // Stored by name so the table reads well in SQL and so renumbering the enum cannot
        // silently rewrite history.
        builder.Property(entry => entry.Action)
            .HasConversion<string>()
            .HasMaxLength(AuditLogEntry.ActionMaxLength)
            .IsRequired();

        builder.Property(entry => entry.Details).IsRequired();

        builder.HasOne(entry => entry.League)
            .WithMany()
            .HasForeignKey(entry => entry.LeagueId)
            .IsRequired();

        builder.HasOne(entry => entry.ActorMembership)
            .WithMany()
            .HasForeignKey(entry => entry.ActorMembershipId)
            .IsRequired();

        // The audit view is "newest first for one league".
        builder.HasIndex(entry => new { entry.LeagueId, entry.CreatedUtc })
            .IsDescending(false, true);
    }
}
