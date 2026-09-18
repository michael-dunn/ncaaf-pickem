using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Operations;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="DataRefreshStatus"/> to <c>DataRefreshStatus</c>.</summary>
public sealed class DataRefreshStatusConfiguration : IEntityTypeConfiguration<DataRefreshStatus>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DataRefreshStatus> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DataRefreshStatus");
        builder.HasKey(status => status.DataType);

        builder.Property(status => status.DataType)
            .HasConversion<byte>()
            .ValueGeneratedNever();

        builder.Property(status => status.LastError).HasMaxLength(DataRefreshStatus.LastErrorMaxLength);
    }
}
