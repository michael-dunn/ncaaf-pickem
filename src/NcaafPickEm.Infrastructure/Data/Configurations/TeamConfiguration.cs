using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Seasons;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="Team"/> to <c>Teams</c>.</summary>
public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Teams");
        builder.HasKey(team => team.Id);

        builder.Property(team => team.School).HasMaxLength(Team.SchoolMaxLength).IsRequired();
        builder.Property(team => team.Mascot).HasMaxLength(Team.MascotMaxLength);
        builder.Property(team => team.Abbreviation).HasMaxLength(Team.AbbreviationMaxLength);
        builder.Property(team => team.LogoUrl).HasMaxLength(Team.LogoUrlMaxLength);
        builder.Property(team => team.Classification).HasConversion<byte>();

        builder.HasOne(team => team.Conference)
            .WithMany()
            .HasForeignKey(team => team.ConferenceId);

        builder.HasIndex(team => team.CfbdId).IsUnique();
        builder.HasIndex(team => team.EspnTeamId);
        builder.HasIndex(team => team.Classification);
    }
}
