using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepDrftWeb.Services.Data;

public class DeepDrftContextFactory : IDesignTimeDbContextFactory<DeepDrftContext>
{
    public DeepDrftContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DeepDrftContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=deepdrft_dev;Username=postgres;Password=postgres");
        
        return new DeepDrftContext(optionsBuilder.Options);
    }
}