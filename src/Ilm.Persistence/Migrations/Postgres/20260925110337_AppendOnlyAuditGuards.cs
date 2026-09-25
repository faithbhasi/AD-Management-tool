using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ilm.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AppendOnlyAuditGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Database-level append-only enforcement. The application role additionally has no UPDATE/DELETE grants
            // on these tables (deploy/postgres/20-grants.sql). A superuser can still disable triggers; the keyed MAC and
            // the off-box audit copy detect that.
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION ilm.ilm_reject_history_change() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'ILM history table % is append-only (% refused)', TG_TABLE_NAME, TG_OP USING ERRCODE = '42501';
END;
$$;");
            foreach (var table in new[] { "AuditRecords", "LeaverTransitions" })
            {
                migrationBuilder.Sql($@"
CREATE TRIGGER ""{table}_no_update_delete"" BEFORE UPDATE OR DELETE ON ilm.""{table}""
  FOR EACH ROW EXECUTE FUNCTION ilm.ilm_reject_history_change();
CREATE TRIGGER ""{table}_no_truncate"" BEFORE TRUNCATE ON ilm.""{table}""
  FOR EACH STATEMENT EXECUTE FUNCTION ilm.ilm_reject_history_change();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "AuditRecords", "LeaverTransitions" })
            {
                migrationBuilder.Sql($@"DROP TRIGGER IF EXISTS ""{table}_no_update_delete"" ON ilm.""{table}"";
DROP TRIGGER IF EXISTS ""{table}_no_truncate"" ON ilm.""{table}"";");
            }

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS ilm.ilm_reject_history_change();");
        }
    }
}
