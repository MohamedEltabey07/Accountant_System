using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DAL.Migrations
{
    /// <inheritdoc />
    public partial class RedefineMaritimeExternalOrderUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defended with IF EXISTS: raw-SQL indexes are not tracked by the EF snapshot.
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoices_ExternalOrderNumber' AND object_id = OBJECT_ID(N'dbo.Invoices'))
    DROP INDEX [IX_Invoices_ExternalOrderNumber] ON [Invoices];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoices_Customer_ExternalOrderNumber_Active' AND object_id = OBJECT_ID(N'dbo.Invoices'))
    DROP INDEX [IX_Invoices_Customer_ExternalOrderNumber_Active] ON [Invoices];

CREATE UNIQUE INDEX [IX_Invoices_Customer_ExternalOrderNumber_Active]
ON [Invoices] ([CustomerId], [ExternalOrderNumber])
WHERE [Type] = 4 AND [ExternalOrderNumber] IS NOT NULL AND [Status] <> 3;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Invoices_Customer_ExternalOrderNumber_Active' AND object_id = OBJECT_ID(N'dbo.Invoices'))
    DROP INDEX [IX_Invoices_Customer_ExternalOrderNumber_Active] ON [Invoices];

CREATE UNIQUE INDEX [IX_Invoices_ExternalOrderNumber]
ON [Invoices] ([ExternalOrderNumber])
WHERE [Type] = 4 AND [ExternalOrderNumber] IS NOT NULL;
");
        }
    }
}
