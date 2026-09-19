using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Conference"/> to <c>Conferences</c>.</summary>
public sealed class ConferenceConfiguration : IEntityTypeConfiguration<Conference>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Conference> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Conferences");
        builder.HasKey(conference => conference.Id);

        builder.Property(conference => conference.Name).HasMaxLength(Conference.NameMaxLength).IsRequired();
        builder.Property(conference => conference.Abbreviation).HasMaxLength(Conference.AbbreviationMaxLength).IsRequired();
        builder.Property(conference => conference.Classification).HasConversion<byte>();

        builder.HasIndex(conference => conference.CfbdId).IsUnique();
    }
}
