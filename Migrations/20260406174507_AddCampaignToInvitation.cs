using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignToInvitation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CampaignDataverseId",
                table: "SurveyInvitations",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CampaignDataverseId",
                table: "SurveyInvitations");
        }
    }
}
