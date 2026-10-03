namespace SqlServerLab.Domain.Labs;

public sealed class Lab
{
    public Guid Id { get; set; }
    public required string OwnerId { get; set; }

    /// <summary>Display name at creation time (Entra object IDs are not human-readable).</summary>
    public string? OwnerName { get; set; }
    public required string Name { get; set; }
    public required string Region { get; set; }
    public required string ResourceGroupName { get; set; }
    public string? VmResourceId { get; set; }
    public string? SqlVmResourceId { get; set; }
    public LabState State { get; set; }
    public string? StateReason { get; set; }
    public bool IsSimulated { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Optimistic concurrency token, regenerated on every save.</summary>
    public Guid RowVersion { get; set; }

    /// <summary>Marks the lab changed even when no other column changes, so deterministic job keys roll over.</summary>
    public void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
        RowVersion = Guid.NewGuid();
    }
}
