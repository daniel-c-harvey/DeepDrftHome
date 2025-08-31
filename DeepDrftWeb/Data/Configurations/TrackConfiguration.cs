using DeepDrftModels.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeepDrftWeb.Data.Configurations;

public class TrackConfiguration : IEntityTypeConfiguration<TrackEntity>
{
    public void Configure(EntityTypeBuilder<TrackEntity> builder)
    {
        builder.ToTable("track");
        
        builder.HasKey(x => x.Id);
        
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .IsRequired();
            
        builder.Property(x => x.MediaPath)
            .HasColumnName("media_path")
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(x => x.TrackName)
            .HasColumnName("track_name")
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(x => x.Artist)
            .HasColumnName("artist")
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(x => x.Album)
            .HasColumnName("album")
            .HasMaxLength(200);
            
        builder.Property(x => x.Genre)
            .HasColumnName("genre")
            .HasMaxLength(100);
            
        builder.Property(x => x.ReleaseDate)
            .HasColumnName("release_date");
            
        builder.Property(x => x.ImagePath)
            .HasColumnName("image_path")
            .HasMaxLength(500);
    }
}