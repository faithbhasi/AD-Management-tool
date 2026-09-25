using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class TransitionOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                schema: "ilm",
                table: "LeaverTransitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TransitionCount",
                schema: "ilm",
                table: "LeaverRequests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_Ordinal",
                schema: "ilm",
                table: "LeaverTransitions",
                columns: new[] { "LeaverRequestId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_Ordinal",
                schema: "ilm",
                table: "LeaverTransitions");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                schema: "ilm",
                table: "LeaverTransitions");

            migrationBuilder.DropColumn(
                name: "TransitionCount",
                schema: "ilm",
                table: "LeaverRequests");
        }
    }
}
