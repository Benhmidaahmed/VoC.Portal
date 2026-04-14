using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Xrmbox.VoC.Portal.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveysTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.CreateTable(
            //    name: "Surveys",
            //    columns: table => new
            //    {
            //        Id = table.Column<int>(type: "int", nullable: false)
            //            .Annotation("SqlServer:Identity", "1, 1"),
            //        DataverseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
            //        JsonContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
            //        IsActive = table.Column<bool>(type: "bit", nullable: false),
            //        LastSync = table.Column<DateTime>(type: "datetime2", nullable: false)
            //    },
            //    constraints: table =>
            //    {
            //        table.PrimaryKey("PK_Surveys", x => x.Id);
            //    });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Surveys");
        }
    }
}
