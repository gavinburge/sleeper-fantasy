namespace SleeperFantasy.Domain.Entities;

public sealed class PlayerIdentityMapping
{
    public Guid Id { get; set; }
    public required string SleeperPlayerId { get; set; }
    public required string Provider { get; set; }
    public required string ProviderPlayerId { get; set; }
    public decimal Confidence { get; set; }
    public bool IsAmbiguous { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
