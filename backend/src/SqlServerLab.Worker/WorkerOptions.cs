using System.ComponentModel.DataAnnotations;

namespace SqlServerLab.Worker;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    /// <summary>Stable identity of this worker instance; defaults to machine name + process ID.</summary>
    public string? WorkerId { get; set; }

    [Range(50, 60_000)] public int PollIntervalMs { get; set; } = 1000;
    [Range(1, 600)] public int LeaseSeconds { get; set; } = 30;
    [Range(50, 300_000)] public int HeartbeatIntervalMs { get; set; } = 10_000;
    [Range(20, 60_000)] public int OperationPollIntervalMs { get; set; } = 1000;
    [Range(1, 240)] public int OperationTimeoutMinutes { get; set; } = 45;
    [Range(1, 3600)] public int ExpiryScanSeconds { get; set; } = 30;

    public string ResolveWorkerId() => WorkerId ?? $"{Environment.MachineName}-{Environment.ProcessId}";
}
