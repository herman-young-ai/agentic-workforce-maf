using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgenticWorkforce.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4ArtifactAndDocumentRetraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "retracted_at",
                table: "project_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retracted_by",
                table: "project_documents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "retracted_at",
                table: "project_artifacts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retracted_by",
                table: "project_artifacts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retracted_at",
                table: "project_documents");

            migrationBuilder.DropColumn(
                name: "retracted_by",
                table: "project_documents");

            migrationBuilder.DropColumn(
                name: "retracted_at",
                table: "project_artifacts");

            migrationBuilder.DropColumn(
                name: "retracted_by",
                table: "project_artifacts");
        }
    }
}
