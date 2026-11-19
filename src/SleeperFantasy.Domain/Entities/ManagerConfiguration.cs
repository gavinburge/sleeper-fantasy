namespace SleeperFantasy.Domain.Entities;

public sealed class ManagerConfiguration
{
    public Guid Id { get; set; }
    public required string UserId { get; set; }
    public required string SleeperUsername { get; set; }
    public required string SleeperUserId { get; set; }
    public required string LeagueId { get; set; }
    public required string TeamName { get; set; }
    public string? RosterId { get; set; }
    public int Season { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
