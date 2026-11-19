using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SleeperFantasy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalysisMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "AnalysisResults",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsStale",
                table: "AnalysisResults",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SourceManifestJson",
                table: "AnalysisResults",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "Availability",
                table: "AnalysisEvidence",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IdentityKey",
                table: "AnalysisEvidence",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStale",
                table: "AnalysisEvidence",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "IsStale",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "SourceManifestJson",
                table: "AnalysisResults");

            migrationBuilder.DropColumn(
                name: "Availability",
                table: "AnalysisEvidence");

            migrationBuilder.DropColumn(
                name: "IdentityKey",
                table: "AnalysisEvidence");

            migrationBuilder.DropColumn(
                name: "IsStale",
                table: "AnalysisEvidence");
        }
    }
}
