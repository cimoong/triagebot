using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TriageBot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    RequesterEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Urgency = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DraftReply = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tickets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRuns_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepIndex = table.Column<int>(type: "integer", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ArgumentsJson = table.Column<string>(type: "text", nullable: true),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentSteps_AgentRuns_AgentRunId",
                        column: x => x.AgentRunId,
                        principalTable: "AgentRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Tickets",
                columns: new[] { "Id", "Body", "Category", "CreatedAtUtc", "DraftReply", "RequesterEmail", "Status", "Subject", "UpdatedAtUtc", "Urgency" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111101"), "I keep getting 'invalid credentials' on the staff portal even though my password is correct.", "AccountAccess", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "dewi@contoso.com", "New", "Cannot login to portal", null, "High" },
                    { new Guid("11111111-1111-1111-1111-111111111102"), "The corporate VPN drops every few minutes when I work from home, making it hard to stay connected.", "Network", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "budi@contoso.com", "New", "VPN keeps disconnecting", null, "Medium" },
                    { new Guid("11111111-1111-1111-1111-111111111103"), "Please install Visual Studio 2022 Professional on my workstation for the new project.", "Software", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "arif@contoso.com", "New", "Request: install Visual Studio", null, "Low" },
                    { new Guid("11111111-1111-1111-1111-111111111104"), "The shared printer near the 3rd floor kitchen shows as offline and nobody can print.", "Hardware", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "siti@contoso.com", "New", "Printer offline 3rd floor", null, "Medium" },
                    { new Guid("11111111-1111-1111-1111-111111111105"), "The order-management application is returning 500 errors for everyone. This is a full outage affecting production.", "Software", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "ops@contoso.com", "New", "URGENT: production app down for all users", null, "Critical" },
                    { new Guid("11111111-1111-1111-1111-111111111106"), "I forgot my Windows password and cannot sign in to my laptop. Please help me reset it.", "AccountAccess", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "rina@contoso.com", "New", "Forgot password", null, "Medium" },
                    { new Guid("11111111-1111-1111-1111-111111111107"), "My Outlook mailbox stopped syncing on my iPhone since yesterday; new messages only appear on the desktop.", "Email", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "tono@contoso.com", "New", "Email not syncing on phone", null, "Low" },
                    { new Guid("11111111-1111-1111-1111-111111111108"), "Onboarding a new analyst next week; please provision a standard laptop and accessories.", "Hardware", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "hr@contoso.com", "New", "New laptop request", null, "Low" },
                    { new Guid("11111111-1111-1111-1111-111111111109"), "Several files on the finance share were renamed with a strange extension and a ransom note appeared. Possible ransomware.", "Other", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "finance@contoso.com", "New", "Suspected data breach in finance share", null, "Critical" },
                    { new Guid("11111111-1111-1111-1111-111111111110"), "Wireless access points on the 2nd floor are unreachable; about 20 people have no connectivity.", "Network", new DateTime(2025, 1, 1, 9, 0, 0, 0, DateTimeKind.Utc), null, "facilities@contoso.com", "New", "Wi-Fi down across 2nd floor", null, "High" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRuns_TicketId",
                table: "AgentRuns",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_AgentRunId",
                table: "AgentSteps",
                column: "AgentRunId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentSteps_AgentRunId_StepIndex",
                table: "AgentSteps",
                columns: new[] { "AgentRunId", "StepIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status",
                table: "Tickets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentSteps");

            migrationBuilder.DropTable(
                name: "AgentRuns");

            migrationBuilder.DropTable(
                name: "Tickets");
        }
    }
}
