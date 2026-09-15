using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SonaFlyUI.Server.Infrastructure.Data;

/// <summary>
/// How the EF tools build a context, rather than starting the application to get one.
///
/// Without this, `dotnet ef` runs Program.cs: it would need a JWT secret, a deployment
/// configuration and a real database path just to compare the model against the migrations.
/// None of that has anything to do with the model, and requiring it makes the migration
/// check fail for reasons that are not migration drift.
///
/// The connection string is never opened for `migrations add` or
/// `has-pending-model-changes` — only the provider matters, so that the model is built the
/// same way SQLite builds it at runtime.
/// </summary>
internal sealed class SonaFlyDbContextFactory : IDesignTimeDbContextFactory<SonaFlyDbContext>
{
    public SonaFlyDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SonaFlyDbContext>()
            .UseSqlite("Data Source=sonafly-design-time.db")
            .Options);
}
