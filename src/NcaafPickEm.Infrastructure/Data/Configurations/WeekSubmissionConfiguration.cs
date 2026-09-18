using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Picks;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="WeekSubmission"/> to <c>WeekSubmissions</c>.</summary>
public sealed class WeekSubmissionConfiguration : IEntityTypeConfiguration<WeekSubmission>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WeekSubmission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WeekSubmissions");
        builder.HasKey(submission => new { submission.MembershipId, submission.WeekGameSetId });

        builder.Property(submission => submission.Status).HasConversion<byte>();

        builder.HasOne(submission => submission.Membership)
            .WithMany()
            .HasForeignKey(submission => submission.MembershipId)
            .IsRequired();

        builder.HasOne(submission => submission.WeekGameSet)
            .WithMany()
            .HasForeignKey(submission => submission.WeekGameSetId)
            .IsRequired();

        // The commissioner status page reads every member's row for one week.
        builder.HasIndex(submission => submission.WeekGameSetId);
    }
}
