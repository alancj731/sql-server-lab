using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SqlServerLab.Migrations.SqlServer.Migrations
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
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DatabaseName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    BackupType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    BlobUri = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Checksum = table.Column<bool>(type: "bit", nullable: false),
                    SqlVersion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FirstLsn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastLsn = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeadlockEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExperimentRunId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    VictimProcessId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawXmlCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ParsedJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadlockEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExperimentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScenarioId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScenarioVersion = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ConfigurationState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarmupPolicy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MeasurementCount = table.Column<int>(type: "int", nullable: false),
                    P50DurationMs = table.Column<double>(type: "float", nullable: true),
                    P95DurationMs = table.Column<double>(type: "float", nullable: true),
                    CpuMs = table.Column<double>(type: "float", nullable: true),
                    LogicalReads = table.Column<long>(type: "bigint", nullable: true),
                    PhysicalReads = table.Column<long>(type: "bigint", nullable: true),
                    RowCount = table.Column<long>(type: "bigint", nullable: true),
                    PlanXmlCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PlanSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WaitSummaryJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExperimentRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Labs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResourceGroupName = table.Column<string>(type: "nvarchar(90)", maxLength: 90, nullable: false),
                    VmResourceId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    SqlVmResourceId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StateReason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    IsSimulated = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Labs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalSimOperations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DurationSeconds = table.Column<double>(type: "float", nullable: false),
                    WillFail = table.Column<bool>(type: "bit", nullable: false),
                    FailureCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResourceId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalSimOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ResolutionSeconds = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<double>(type: "float", nullable: false),
                    RawCounterValue = table.Column<double>(type: "float", nullable: true),
                    SampledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PatchAssessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UpdatesJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchAssessments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LabJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InputJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    CurrentStep = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    MaxAttempts = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    HeartbeatAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NotBefore = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelRequested = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCategory = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ExternalOperationId = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    MutexKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
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
