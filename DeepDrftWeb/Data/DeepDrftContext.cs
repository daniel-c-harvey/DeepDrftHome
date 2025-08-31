using DeepDrftModels.Entities;
using DeepDrftWeb.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace DeepDrftWeb.Data;

public class DeepDrftContext : DbContext
{
    public DeepDrftContext(DbContextOptions<DeepDrftContext> options) : base(options)
    {
    }

    public DbSet<TrackEntity> Tracks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.ApplyConfiguration(new TrackConfiguration());
    }
}