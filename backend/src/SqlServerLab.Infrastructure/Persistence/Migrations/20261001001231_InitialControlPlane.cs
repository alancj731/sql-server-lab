using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlServerLab.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialControlPlane : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: true),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActorId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatabaseName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    BackupType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    BlobUri = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Checksum = table.Column<bool>(type: "INTEGER", nullable: false),
                    SqlVersion = table.Column<string>(type: "TEXT", nullable: true),
                    FirstLsn = table.Column<string>(type: "TEXT", nullable: true),
                    LastLsn = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    VerifiedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeadlockEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExperimentRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    VictimProcessId = table.Column<string>(type: "TEXT", nullable: true),
                    RawXmlCompressed = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ParsedJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadlockEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScenarioId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ScenarioVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfigurationState = table.Column<string>(type: "TEXT", nullable: true),
                    ParametersJson = table.Column<string>(type: "TEXT", nullable: true),
                    WarmupPolicy = table.Column<string>(type: "TEXT", nullable: true),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    MeasurementCount = table.Column<int>(type: "INTEGER", nullable: false),
                    P50DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    P95DurationMs = table.Column<double>(type: "REAL", nullable: true),
                    CpuMs = table.Column<double>(type: "REAL", nullable: true),
                    LogicalReads = table.Column<long>(type: "INTEGER", nullable: true),
                    PhysicalReads = table.Column<long>(type: "INTEGER", nullable: true),
                    RowCount = table.Column<long>(type: "INTEGER", nullable: true),
                    PlanXmlCompressed = table.Column<byte[]>(type: "BLOB", nullable: true),
                    PlanSummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    WaitSummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Region = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResourceGroupName = table.Column<string>(type: "TEXT", maxLength: 90, nullable: false),
                    VmResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    SqlVmResourceId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StateReason = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    IsSimulated = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpiresAt = table.Column<long>(type: "INTEGER", nullable: false),
                    RowVersion = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Labs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalSimOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DurationSeconds = table.Column<double>(type: "REAL", nullable: false),
                    WillFail = table.Column<bool>(type: "INTEGER", nullable: false),
                    FailureCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    FailureMessage = table.Column<string>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSimOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ResolutionSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    RawCounterValue = table.Column<double>(type: "REAL", nullable: true),
                    SampledAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatchAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AssessedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    UpdatesJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LabId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStep = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxAttempts = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaseOwner = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    HeartbeatAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CompletedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    NotBefore = table.Column<long>(type: "INTEGER", nullable: false),
                    CancelRequested = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorCategory = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalOperationId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    MutexKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabJobs_Labs_LabId",
                        column: x => x.LabId,
                        principalTable: "Labs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_LabId_OccurredAt",
                table: "AuditEvents",
                columns: new[] { "LabId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExperimentRuns_LabId_ScenarioId",
                table: "ExperimentRuns",
                columns: new[] { "LabId", "ScenarioId" });

            migrationBuilder.CreateIndex(
                name: "IX_LabJobs_IdempotencyKey",
                table: "LabJobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabJobs_LabId_RequestedAt",
                table: "LabJobs",
                columns: new[] { "LabId", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LabJobs_MutexKey",
                table: "LabJobs",
                column: "MutexKey",
                unique: true,
                filter: "[MutexKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LabJobs_Status_NotBefore",
                table: "LabJobs",
                columns: new[] { "Status", "NotBefore" });

            migrationBuilder.CreateIndex(
                name: "IX_LabJobs_UpdatedAt",
                table: "LabJobs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Labs_ExpiresAt",
                table: "Labs",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Labs_OwnerId_State",
                table: "Labs",
                columns: new[] { "OwnerId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Labs_UpdatedAt",
                table: "Labs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_LocalSimOperations_LabId_StartedAt",
                table: "LocalSimOperations",
                columns: new[] { "LabId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSamples_LabId_Name_SampledAt",
                table: "MetricSamples",
                columns: new[] { "LabId", "Name", "SampledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "BackupRecords");

            migrationBuilder.DropTable(
                name: "DeadlockEvents");

            migrationBuilder.DropTable(
                name: "ExperimentRuns");

            migrationBuilder.DropTable(
                name: "LabJobs");

            migrationBuilder.DropTable(
                name: "LocalSimOperations");

            migrationBuilder.DropTable(
                name: "MetricSamples");

            migrationBuilder.DropTable(
                name: "PatchAssessments");

            migrationBuilder.DropTable(
                name: "Labs");
        }
    }
}
