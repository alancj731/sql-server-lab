namespace SqlServerLab.Domain.Jobs;

public enum LabJobType
{
    ProvisionLab,
    StartVm,
    DeallocateVm,
    DeleteLab,
    SeedDatabase,
    ApplyIndex,
    RemoveIndex,
    RunIndexBenchmark,
    RunDeadlock,
    RunDeadlockResolution,
    CollectMetrics,
    CreateBackup,
    VerifyBackup,
    AssessPatches,
    InstallPatches,
    ReconcileLab,
}
