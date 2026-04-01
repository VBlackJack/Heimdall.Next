namespace TwinShell.Core.Interfaces;

/// <summary>
/// Service for seeding initial data
/// </summary>
public interface ISeedService
{
    Task SeedAsync();
}
