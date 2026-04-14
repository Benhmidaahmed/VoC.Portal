using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        //    migrationBuilder.CreateTable(
        //        name: "Campaigns",
        //        columns: table => new
        //        {
        //            DataverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
        //            Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
        //            StateCode = table.Column<int>(type: "int", nullable: false),
        //            StatusCode = table.Column<int>(type: "int", nullable: false),
        //            SurveyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
        //            SurveyId1 = table.Column<int>(type: "int", nullable: true),
        //            LastSync = table.Column<DateTime>(type: "datetime2", nullable: false)
        //        },
        //        constraints: table =>
        //        {
        //            table.PrimaryKey("PK_Campaigns", x => x.DataverseId);
        //            table.ForeignKey(
        //                name: "FK_Campaigns_Surveys_SurveyId1",
        //                column: x => x.SurveyId1,
        //                principalTable: "Surveys",
        //                principalColumn: "Id");
        //        });

        //    migrationBuilder.CreateIndex(
        //        name: "IX_Campaigns_SurveyId1",
        //        table: "Campaigns",
        //        column: "SurveyId1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Campaigns");
        }
    }
}
