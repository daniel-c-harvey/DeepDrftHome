using Data.Data.Configurations;
using DeepDrftModels.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepDrftData.Data.Configurations;

public class TrackConfiguration : BaseEntityConfiguration<TrackEntity>
{
    public override void Configure(EntityTypeBuilder<TrackEntity> builder)
    {
        // Wires up Id PK + audit columns (CreatedAt, UpdatedAt, IsDeleted) and the IsDeleted index.
        base.Configure(builder);

        builder.ToTable("track");

        // Map the base audit columns to the snake_case naming the rest of the schema uses.
        builder.Property(e => e.Id).HasColumnName("id");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at");
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        builder.Property(e => e.IsDeleted).HasColumnName("is_deleted");

        builder.Property(e => e.EntryKey)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("entry_key");

        builder.Property(e => e.TrackName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("track_name");

        builder.Property(e => e.Artist)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("artist");

        builder.Property(e => e.Album)
            .HasMaxLength(200)
            .HasColumnName("album");

        builder.Property(e => e.Genre)
            .HasMaxLength(100)
            .HasColumnName("genre");

        builder.Property(e => e.ReleaseDate)
            .HasColumnName("release_date");

        builder.Property(e => e.ImagePath)
            .HasMaxLength(500)
            .HasColumnName("image_path");

        builder.Property(e => e.CreatedByUserId)
            .HasColumnName("created_by_user_id");

        // Names the is_deleted index explicitly. BaseEntityConfiguration.Configure already
        // calls HasIndex(e => e.IsDeleted); this adds HasDatabaseName so EF always uses
        // "IX_track_is_deleted" regardless of auto-naming conventions.
        builder.HasIndex(e => e.IsDeleted).HasDatabaseName("IX_track_is_deleted");
    }
}
