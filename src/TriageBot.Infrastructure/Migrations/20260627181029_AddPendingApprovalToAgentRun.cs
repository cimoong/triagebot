using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TriageBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingApprovalToAgentRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingArgumentsJson",
                table: "AgentRuns",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PendingToolName",
                table: "AgentRuns",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PendingArgumentsJson",
                table: "AgentRuns");

            migrationBuilder.DropColumn(
                name: "PendingToolName",
                table: "AgentRuns");
        }
    }
}
