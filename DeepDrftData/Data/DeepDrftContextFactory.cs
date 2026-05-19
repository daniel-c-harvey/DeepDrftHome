using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepDrftData.Data;

public class DeepDrftContextFactory : IDesignTimeDbContextFactory<DeepDrftContext>
{
    public DeepDrftContext CreateDbContext(string[] args)
    {
        // For 'dotnet ef' commands, set ConnectionStrings__DefaultConnection in your environment when
        // you need to actually hit the database (e.g. `dotnet ef database update`). For model-only
        // operations like `migrations add`, the placeholder below is sufficient — EF never connects.
        // Example: export ConnectionStrings__DefaultConnection="Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=yourpassword"
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5433;Database=postgres;Username=postgres;Password=placeholder";

        var optionsBuilder = new DbContextOptionsBuilder<DeepDrftContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DeepDrftContext(optionsBuilder.Options);
    }
}
