using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignRelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Surveys_SurveyId1",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_SurveyId1",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "SurveyId1",
                table: "Campaigns");

            migrationBuilder.RenameColumn(
                name: "SurveyId",
                table: "Campaigns",
                newName: "SurveyDataverseId");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Surveys_DataverseId",
                table: "Surveys",
                column: "DataverseId");

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_SurveyDataverseId",
                table: "Campaigns",
                column: "SurveyDataverseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Surveys_SurveyDataverseId",
                table: "Campaigns",
                column: "SurveyDataverseId",
                principalTable: "Surveys",
                principalColumn: "DataverseId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_Surveys_SurveyDataverseId",
                table: "Campaigns");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Surveys_DataverseId",
                table: "Surveys");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_SurveyDataverseId",
                table: "Campaigns");

            migrationBuilder.RenameColumn(
                name: "SurveyDataverseId",
                table: "Campaigns",
                newName: "SurveyId");

            migrationBuilder.AddColumn<int>(
                name: "SurveyId1",
                table: "Campaigns",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_SurveyId1",
                table: "Campaigns",
                column: "SurveyId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_Surveys_SurveyId1",
                table: "Campaigns",
                column: "SurveyId1",
                principalTable: "Surveys",
                principalColumn: "Id");
        }
    }
}
