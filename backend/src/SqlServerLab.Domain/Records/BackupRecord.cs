namespace SqlServerLab.Domain.Records;

/// <summary>Backup metadata. Populated from Milestone 8 onward. Blob URIs never contain credentials.</summary>
public sealed class BackupRecord
{
    public Guid Id { get; set; }
    public Guid LabId { get; set; }
    public required string DatabaseName { get; set; }
    public required string BackupType { get; set; }
    public required string Status { get; set; }
    public string? BlobUri { get; set; }
    public long? SizeBytes { get; set; }
    public bool Checksum { get; set; }
    public string? SqlVersion { get; set; }
    public string? FirstLsn { get; set; }
    public string? LastLsn { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
}
