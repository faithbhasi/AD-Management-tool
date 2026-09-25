using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class TransitionOrdinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ordinal",
                table: "LeaverTransitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TransitionCount",
                table: "LeaverRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_Ordinal",
                table: "LeaverTransitions",
                columns: new[] { "LeaverRequestId", "Ordinal" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LeaverTransitions_LeaverRequestId_Ordinal",
                table: "LeaverTransitions");

            migrationBuilder.DropColumn(
                name: "Ordinal",
                table: "LeaverTransitions");

            migrationBuilder.DropColumn(
                name: "TransitionCount",
                table: "LeaverRequests");
        }
    }
}
