using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignEmailsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvitationBody",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "InvitationSubject",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ReminderBody",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "ReminderSubject",
                table: "Campaigns");

            migrationBuilder.CreateTable(
                name: "CampaignEmails",
                columns: table => new
                {
                    DataverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    DelayDays = table.Column<int>(type: "int", nullable: true),
                    CampaignId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignEmails", x => x.DataverseId);
                    table.ForeignKey(
                        name: "FK_CampaignEmails_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "DataverseId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignEmails_CampaignId",
                table: "CampaignEmails",
                column: "CampaignId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignEmails");

            migrationBuilder.AddColumn<string>(
                name: "InvitationBody",
                table: "Campaigns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvitationSubject",
                table: "Campaigns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReminderBody",
                table: "Campaigns",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReminderSubject",
                table: "Campaigns",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
