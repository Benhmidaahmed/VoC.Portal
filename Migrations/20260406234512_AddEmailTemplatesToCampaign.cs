using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTemplatesToCampaign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.AddColumn<string>(
            //    name: "InvitationBody",
            //    table: "Campaigns",
            //    type: "nvarchar(max)",
            //    nullable: true);

            //migrationBuilder.AddColumn<string>(
            //    name: "InvitationSubject",
            //    table: "Campaigns",
            //    type: "nvarchar(max)",
            //    nullable: true);

            //migrationBuilder.AddColumn<string>(
            //    name: "ReminderBody",
            //    table: "Campaigns",
            //    type: "nvarchar(max)",
            //    nullable: true);

            //migrationBuilder.AddColumn<string>(
            //    name: "ReminderSubject",
            //    table: "Campaigns",
            //    type: "nvarchar(max)",
            //    nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
        }
    }
}
