using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SleeperFantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisExecutionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "AnalysisRuns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RetryOfRunId",
                table: "AnalysisRuns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_RetryOfRunId",
                table: "AnalysisRuns",
                column: "RetryOfRunId");

            migrationBuilder.AddForeignKey(
                name: "FK_AnalysisRuns_AnalysisRuns_RetryOfRunId",
                table: "AnalysisRuns",
                column: "RetryOfRunId",
                principalTable: "AnalysisRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AnalysisRuns_AnalysisRuns_RetryOfRunId",
                table: "AnalysisRuns");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisRuns_RetryOfRunId",
                table: "AnalysisRuns");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AnalysisRuns");

            migrationBuilder.DropColumn(
                name: "RetryOfRunId",
                table: "AnalysisRuns");

        }
    }
}
