using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceAdditionalChargesAndAmendments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_ExternalOrderNumber",
                table: "Invoices");

            migrationBuilder.CreateTable(
                name: "InvoiceAdditionalCharge",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsBillableToCompany = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAdditionalCharge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAdditionalCharge_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceAmendment",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    AmendedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AmendedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceAmendment", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceAmendment_AspNetUsers_AmendedByUserId",
                        column: x => x.AmendedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InvoiceAmendment_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Customer_ExternalOrderNumber",
                table: "Invoices",
                columns: new[] { "CustomerId", "ExternalOrderNumber" },
                filter: "[Type] = 4 AND [ExternalOrderNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAdditionalCharge_InvoiceId",
                table: "InvoiceAdditionalCharge",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAmendment_AmendedByUserId",
                table: "InvoiceAmendment",
                column: "AmendedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceAmendment_InvoiceId",
                table: "InvoiceAmendment",
                column: "InvoiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceAdditionalCharge");

            migrationBuilder.DropTable(
                name: "InvoiceAmendment");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Customer_ExternalOrderNumber",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId",
                table: "Invoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ExternalOrderNumber",
                table: "Invoices",
                column: "ExternalOrderNumber",
                unique: true,
                filter: "[Type] = 4 AND [ExternalOrderNumber] IS NOT NULL");
        }
    }
}
