using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NcaafPickEm.Domain.Users;

namespace NcaafPickEm.Infrastructure.Data.Configurations;

/// <summary>Maps <see cref="User"/> to <c>Users</c>.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.ExternalSubject).HasMaxLength(User.ExternalSubjectMaxLength).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.DisplayName).HasMaxLength(User.DisplayNameMaxLength).IsRequired();

        builder.HasIndex(user => user.ExternalSubject).IsUnique();
        builder.HasIndex(user => user.Email).IsUnique();
    }
}
