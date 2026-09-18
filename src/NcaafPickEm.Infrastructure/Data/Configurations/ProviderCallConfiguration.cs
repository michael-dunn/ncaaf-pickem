using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Operations;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="ProviderCall"/> to <c>ProviderCalls</c>.</summary>
public sealed class ProviderCallConfiguration : IEntityTypeConfiguration<ProviderCall>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProviderCall> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ProviderCalls");
        builder.HasKey(call => call.Id);

        builder.Property(call => call.Provider).HasMaxLength(ProviderCall.ProviderMaxLength).IsRequired();
        builder.Property(call => call.Operation).HasMaxLength(ProviderCall.OperationMaxLength).IsRequired();
        builder.Property(call => call.Error).HasMaxLength(ProviderCall.ErrorMaxLength);

        // The CFBD free-tier counter is a COUNT over one provider for one month.
        builder.HasIndex(call => new { call.Provider, call.StartedUtc });
    }
}
