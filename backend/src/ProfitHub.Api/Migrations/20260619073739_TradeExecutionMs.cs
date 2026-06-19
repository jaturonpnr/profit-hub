using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProfitHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TradeExecutionMs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExecutionMs",
                table: "Trades",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExecutionMs",
                table: "Trades");
        }
    }
}
