using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SleeperFantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisRunActiveUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_ManagerConfigurationId",
                table: "AnalysisRuns",
                column: "ManagerConfigurationId",
                unique: true,
                filter: "\"Status\" IN ('queued', 'running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalysisRuns_ManagerConfigurationId",
                table: "AnalysisRuns");
        }
    }
}
