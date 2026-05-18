using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepDrftWeb.Services.Data;

public class DeepDrftContextFactory : IDesignTimeDbContextFactory<DeepDrftContext>
{
    public DeepDrftContext CreateDbContext(string[] args)
    {
        // For 'dotnet ef' commands, set ConnectionStrings__DefaultConnection in your environment.
        // Example: export ConnectionStrings__DefaultConnection="Host=localhost;Database=deepdrft_dev;Username=postgres;Password=yourpassword"
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "Set ConnectionStrings__DefaultConnection environment variable to run dotnet ef commands. " +
                "Example: Host=localhost;Database=deepdrft_dev;Username=postgres;Password=yourpassword");

        var optionsBuilder = new DbContextOptionsBuilder<DeepDrftContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DeepDrftContext(optionsBuilder.Options);
    }
}