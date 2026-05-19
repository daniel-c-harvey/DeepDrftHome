using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeepDrftData.Data;

public class DeepDrftContextFactory : IDesignTimeDbContextFactory<DeepDrftContext>
{
    public DeepDrftContext CreateDbContext(string[] args)
    {
        // Load the real connection string from environment/connections.json — the same
        // file DeepDrftWeb's Program.cs loads via CredentialTools. When EF tools run with
        // --startup-project DeepDrftWeb, the working directory resolves there, so this
        // relative path works without any env var configuration.
        const string relPath = "environment/connections.json";
        if (!File.Exists(relPath))
            throw new FileNotFoundException(
                $"'{relPath}' not found. Run EF commands with --startup-project DeepDrftWeb " +
                $"from the solution root (current dir: {Directory.GetCurrentDirectory()}).", relPath);

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(relPath));
        var connectionString = doc.RootElement
            .GetProperty("ConnectionStrings")
            .GetProperty("DefaultConnection")
            .GetString()
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection not found in environment/connections.json");

        var optionsBuilder = new DbContextOptionsBuilder<DeepDrftContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new DeepDrftContext(optionsBuilder.Options);
    }
}
