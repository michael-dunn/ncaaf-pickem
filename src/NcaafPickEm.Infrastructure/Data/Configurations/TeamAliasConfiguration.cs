using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="TeamAlias"/> to <c>TeamAliases</c>.</summary>
public sealed class TeamAliasConfiguration : IEntityTypeConfiguration<TeamAlias>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TeamAlias> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TeamAliases");
        builder.HasKey(alias => alias.Id);

        builder.Property(alias => alias.Alias).HasMaxLength(TeamAlias.AliasMaxLength).IsRequired();
        builder.Property(alias => alias.Source).HasConversion<byte>();

        builder.HasOne(alias => alias.Team)
            .WithMany()
            .HasForeignKey(alias => alias.TeamId)
            .IsRequired();

        builder.HasIndex(alias => new { alias.Source, alias.Alias }).IsUnique();
    }
}
