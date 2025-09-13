using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepDrftWeb.Services.Data;

public class DeepDrftContextFactory : IDesignTimeDbContextFactory<DeepDrftContext>
{
    public DeepDrftContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DeepDrftContext>();
        optionsBuilder.UseSqlite("Data Source=../Database/deepdrft.db");
        
        return new DeepDrftContext(optionsBuilder.Options);
    }
}