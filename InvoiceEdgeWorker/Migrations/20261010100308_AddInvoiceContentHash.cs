using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InvoiceEdgeWorker.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceContentHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ContentHash",
                table: "Invoices",
                column: "ContentHash",
                unique: true,
                filter: "\"ContentHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_ContentHash",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Invoices");
        }
    }
}
