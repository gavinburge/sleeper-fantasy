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
            migrationBuilder.Sql("""
                ALTER TABLE "AnalysisResults"
                    ADD COLUMN IF NOT EXISTS "Confidence" numeric(5,4) NOT NULL DEFAULT 0.0,
                    ADD COLUMN IF NOT EXISTS "IsStale" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "SourceManifestJson" jsonb NOT NULL DEFAULT '{}'::jsonb;
                ALTER TABLE "AnalysisEvidence"
                    ADD COLUMN IF NOT EXISTS "Availability" character varying(30) NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "IdentityKey" character varying(200),
                    ADD COLUMN IF NOT EXISTS "IsStale" boolean NOT NULL DEFAULT false;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "AnalysisResults"
                    DROP COLUMN IF EXISTS "Confidence",
                    DROP COLUMN IF EXISTS "IsStale",
                    DROP COLUMN IF EXISTS "SourceManifestJson";
                ALTER TABLE "AnalysisEvidence"
                    DROP COLUMN IF EXISTS "Availability",
                    DROP COLUMN IF EXISTS "IdentityKey",
                    DROP COLUMN IF EXISTS "IsStale";
                """);
        }
    }
}
