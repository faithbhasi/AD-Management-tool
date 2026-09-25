using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class AppendOnlyAuditGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite (development only): triggers give the same append-only behaviour locally.
            foreach (var table in new[] { "AuditRecords", "LeaverTransitions" })
            {
                migrationBuilder.Sql($@"CREATE TRIGGER ""{table}_no_update"" BEFORE UPDATE ON ""{table}""
BEGIN SELECT RAISE(ABORT, 'ILM history table {table} is append-only'); END;");
                migrationBuilder.Sql($@"CREATE TRIGGER ""{table}_no_delete"" BEFORE DELETE ON ""{table}""
BEGIN SELECT RAISE(ABORT, 'ILM history table {table} is append-only'); END;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "AuditRecords", "LeaverTransitions" })
            {
                migrationBuilder.Sql($@"DROP TRIGGER IF EXISTS ""{table}_no_update"";");
                migrationBuilder.Sql($@"DROP TRIGGER IF EXISTS ""{table}_no_delete"";");
            }
        }
    }
}
