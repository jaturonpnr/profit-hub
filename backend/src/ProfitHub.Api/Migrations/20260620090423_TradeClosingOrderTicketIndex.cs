using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProfitHub.Api.Migrations
{
    /// <inheritdoc />
    public partial class TradeClosingOrderTicketIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Trades_ClosingOrderTicket",
                table: "Trades",
                column: "ClosingOrderTicket");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trades_ClosingOrderTicket",
                table: "Trades");
        }
    }
}
