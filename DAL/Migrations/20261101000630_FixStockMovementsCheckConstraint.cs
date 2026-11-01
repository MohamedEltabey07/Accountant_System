using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class FixStockMovementsCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_InOut_Positive",
                table: "StockMovements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_InOut_Positive",
                table: "StockMovements",
                sql: "([MovementType] = 1 AND [TotalWeightKg] > 0 AND [Cartons] >= 0) OR ([MovementType] = 2 AND [TotalWeightKg] < 0 AND [Cartons] <= 0) OR ([MovementType] = 3 AND ([Cartons] <> 0 OR [TotalWeightKg] <> 0))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_StockMovements_InOut_Positive",
                table: "StockMovements");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockMovements_InOut_Positive",
                table: "StockMovements",
                sql: "([MovementType] IN (1, 2) AND ([Cartons] > 0 AND [TotalWeightKg] > 0)) OR ([MovementType] = 3 AND ([Cartons] <> 0 OR [TotalWeightKg] <> 0))");
        }
    }
}
