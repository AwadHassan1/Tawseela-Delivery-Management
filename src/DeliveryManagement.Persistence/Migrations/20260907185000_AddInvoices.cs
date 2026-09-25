using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryManagement.Persistence.Migrations;

/// <summary>
/// Adds invoice storage without dropping or recreating any existing Tawseela tables.
/// The application also contains an idempotent startup schema patch for legacy databases
/// that were created with EnsureCreated before migrations were introduced.
/// </summary>
public partial class AddInvoices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Invoices",
            columns: table => new
            {
                Id = table.Column<int>(type: "int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                OrderId = table.Column<int>(type: "int", nullable: false),
                CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CustomerPhone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                CustomerAddress = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                OrderType = table.Column<int>(type: "int", nullable: false),
                OrderValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                DeliveryFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Total = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                OrderStatus = table.Column<int>(type: "int", nullable: false),
                DeliveryManName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                CollectedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                IsCollected = table.Column<bool>(type: "bit", nullable: false),
                Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Invoices", x => x.Id);
                table.ForeignKey("FK_Invoices_Orders", x => x.OrderId, "Orders", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_Invoices_InvoiceNumber", table: "Invoices", column: "InvoiceNumber", unique: true);
        migrationBuilder.CreateIndex(name: "IX_Invoices_OrderId", table: "Invoices", column: "OrderId", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "Invoices");
}
