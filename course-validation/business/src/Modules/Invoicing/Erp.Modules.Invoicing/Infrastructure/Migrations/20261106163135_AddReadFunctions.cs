using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Invoicing.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("invoicing.get_company_profile.v1.sql"));
            migrationBuilder.Sql(Sql("invoicing.receivables_aging.v1.sql"));
            migrationBuilder.Sql(Sql("invoicing.search_invoices.v1.sql"));
            migrationBuilder.Sql(Sql("invoicing.withholding_summary.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.get_company_profile();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.list_withholding_certificates(integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.withholding_summary(integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.get_invoice_payments(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.get_invoice_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.get_invoice(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.count_invoices(bigint, integer, text, date);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.search_invoices(bigint, integer, text, date, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS invoicing.receivables_aging(date);");
        }
    }
}
