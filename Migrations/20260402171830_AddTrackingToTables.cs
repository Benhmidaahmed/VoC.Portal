using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackingToTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPartialSave",
                table: "SurveyInvitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReminderSent",
                table: "SurveyInvitations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReminderCount",
                table: "SurveyInvitations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "LocalResponses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Token",
                table: "LocalResponses",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPartialSave",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "LastReminderSent",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "ReminderCount",
                table: "SurveyInvitations");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "LocalResponses");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "LocalResponses");
        }
    }
}
