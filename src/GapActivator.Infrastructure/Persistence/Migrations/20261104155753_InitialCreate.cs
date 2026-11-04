using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GapActivator.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Processes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Lifecycle = table.Column<string>(type: "TEXT", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "INTEGER", nullable: false),
                    RetryPending = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ExecutableKey = table.Column<string>(type: "TEXT", nullable: false),
                    NameKey = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Arguments = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<string>(type: "TEXT", nullable: false),
                    Executable = table.Column<string>(type: "TEXT", nullable: false),
                    WorkingDirectory = table.Column<string>(type: "TEXT", nullable: true),
                    GracePeriod = table.Column<long>(type: "INTEGER", nullable: true),
                    TimeLimit = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessName = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledFor = table.Column<long>(type: "INTEGER", nullable: true),
                    IsLate = table.Column<bool>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    StopReason = table.Column<string>(type: "TEXT", nullable: true),
                    Pid = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessStartTime = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EndedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Outcome = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    Killed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    CycleInterval = table.Column<long>(type: "INTEGER", nullable: false),
                    FailureLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    DefaultTimeLimit = table.Column<long>(type: "INTEGER", nullable: true),
                    DefaultGracePeriod = table.Column<long>(type: "INTEGER", nullable: false),
                    LogRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    HistoryRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LogLevel = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    CheckForUpdates = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPaused = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    NextOccurrenceAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 13, nullable: false),
                    ProcessId = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: true),
                    FulfilledAt = table.Column<long>(type: "INTEGER", nullable: true),
                    Rule = table.Column<string>(type: "TEXT", nullable: true),
                    Anchor = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Processes_ProcessId",
                        column: x => x.ProcessId,
                        principalTable: "Processes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_OccurredAt",
                table: "HistoryEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_RunId",
                table: "HistoryEntries",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_ExecutableKey",
                table: "Processes",
                column: "ExecutableKey");

            migrationBuilder.CreateIndex(
                name: "IX_Processes_NameKey",
                table: "Processes",
                column: "NameKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_CreatedAt",
                table: "Runs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_EndedAt",
                table: "Runs",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProcessId",
                table: "Runs",
                column: "ProcessId",
                unique: true,
                filter: "\"State\" <> 'Completed'");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProcessId_CreatedAt",
                table: "Runs",
                columns: new[] { "ProcessId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProcessId_EndedAt",
                table: "Runs",
                columns: new[] { "ProcessId", "EndedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_ProcessId",
                table: "Schedules",
                column: "ProcessId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoryEntries");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Processes");
        }
    }
}
