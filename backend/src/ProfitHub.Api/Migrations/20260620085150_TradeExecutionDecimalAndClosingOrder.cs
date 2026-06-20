using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProfitHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TradeExecutionDecimalAndClosingOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ExecutionMs",
                table: "Trades",
                type: "numeric(9,3)",
                precision: 9,
                scale: 3,
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ClosingOrderTicket",
                table: "Trades",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosingOrderTicket",
                table: "Trades");

            migrationBuilder.AlterColumn<int>(
                name: "ExecutionMs",
                table: "Trades",
                type: "integer",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,3)",
                oldPrecision: 9,
                oldScale: 3,
                oldNullable: true);
        }
    }
}
