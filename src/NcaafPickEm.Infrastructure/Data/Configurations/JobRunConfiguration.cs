using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Operations;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="JobRun"/> to <c>JobRuns</c>.</summary>
public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("JobRuns");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.JobName).HasMaxLength(JobRun.JobNameMaxLength).IsRequired();
        builder.Property(run => run.Error).HasMaxLength(JobRun.ErrorMaxLength);

        // Idempotency for cron jobs across restarts (D-005): a second attempt at the same
        // occurrence fails the insert instead of running the job twice.
        builder.HasIndex(run => new { run.JobName, run.ScheduledForUtc }).IsUnique();

        builder.HasIndex(run => run.StartedUtc).IsDescending(true);
    }
}
