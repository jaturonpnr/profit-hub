using System.Globalization;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using ProfitHub.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ProfitHub.Api.Features;

public static class Export
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/export").RequireAuthorization();

        g.MapGet("/trades.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic) =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            var tz = Reports.Tz(user);
            var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => a.Name);
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .OrderBy(t => t.CloseTimeUtc).ToListAsync();
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("CloseTime(Local),Account,Symbol,Direction,Lots,OpenPrice,ClosePrice,GrossProfit,Commission,Swap,NetProfit,MagicNumber,Comment\n");
            foreach (var t in trades)
                sb.Append(string.Join(',',
                    TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz).ToString("yyyy-MM-dd HH:mm:ss", inv),
                    Csv(accounts.GetValueOrDefault(t.AccountId, "")), Csv(t.Symbol), t.Direction,
                    t.Lots.ToString(inv), t.OpenPrice.ToString(inv), t.ClosePrice.ToString(inv),
                    t.GrossProfit.ToString(inv), t.Commission.ToString(inv), t.Swap.ToString(inv),
                    t.NetProfit.ToString(inv), t.MagicNumber.ToString(inv), Csv(t.Comment))).Append('\n');
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "trades.csv");
        });

        g.MapGet("/summary.csv", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day") =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            if (!Reports.IsValidPeriod(period))
                return Results.BadRequest(new { error = "period must be day, week or month" });
            var tz = Reports.Tz(user);
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .Select(t => new { t.CloseTimeUtc, t.NetProfit }).ToListAsync();
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("PeriodStart,NetProfit,TradeCount,Wins,WinRate\n");
            foreach (var grp in trades.GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, period)).OrderBy(x => x.Key))
            {
                var count = grp.Count();
                var wins = grp.Count(t => t.NetProfit > 0);
                var winRate = (100.0 * wins / count).ToString("0.0", inv);
                sb.Append(string.Create(inv,
                        $"{grp.Key:yyyy-MM-dd},{grp.Sum(t => t.NetProfit)},{count},{wins},{winRate}%"))
                    .Append('\n');
            }
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"summary-{period}.csv");
        });

        // One-page summary PDF report (QuestPDF). Same filters as the CSV exports.
        g.MapGet("/report.pdf", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic) =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            var tz = Reports.Tz(user);

            // Account names (for the title + standing table). Respect the account filter.
            var accountsQ = db.Accounts.Where(a => a.UserId == user.UserId());
            if (ids.Length > 0) accountsQ = accountsQ.Where(a => ids.Contains(a.Id));
            var accounts = await accountsQ
                .Select(a => new { a.Id, a.Name, a.AccountNumber }).ToListAsync();
            string AccName(Guid id)
            {
                var a = accounts.FirstOrDefault(x => x.Id == id);
                return a is null ? "" : (!string.IsNullOrWhiteSpace(a.Name) ? a.Name : a.AccountNumber.ToString());
            }

            // Filtered trades (for the KPI row + monthly/by-account tables).
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .Select(t => new { t.AccountId, t.CloseTimeUtc, t.NetProfit }).ToListAsync();
            var tradeCount = trades.Count;
            var netPl = trades.Sum(t => t.NetProfit);
            var wins = trades.Count(t => t.NetProfit > 0);
            var winRate = tradeCount > 0 ? 100.0 * wins / tradeCount : 0;

            // Lifetime account standing (mirrors Reports.GetBalances): deposits + realized P/L.
            // Account-filtered, NOT date-filtered.
            var myIds = accounts.Select(a => a.Id).ToHashSet();
            var deposits = await db.BalanceOperations.Where(b => myIds.Contains(b.AccountId))
                .Select(b => new { b.AccountId, b.Amount }).ToListAsync();
            var lifeTrades = await db.Trades.Where(t => myIds.Contains(t.AccountId))
                .Select(t => new { t.AccountId, t.NetProfit }).ToListAsync();
            var depByAcc = deposits.GroupBy(d => d.AccountId).ToDictionary(grp => grp.Key, grp => grp.Sum(x => x.Amount));
            var profByAcc = lifeTrades.GroupBy(t => t.AccountId).ToDictionary(grp => grp.Key, grp => grp.Sum(x => x.NetProfit));
            var standing = accounts.Select(a =>
            {
                var nd = depByAcc.GetValueOrDefault(a.Id, 0m);
                var np = profByAcc.GetValueOrDefault(a.Id, 0m);
                return new StandingRow(AccName(a.Id), nd, nd + np,
                    nd > 0 ? Math.Round(np / nd * 100, 2) : (decimal?)null);
            }).OrderByDescending(r => r.Balance).ToList();

            // Monthly P/L for the range (mirrors GetSummary period=month).
            var monthly = trades.GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, "month"))
                .OrderBy(grp => grp.Key)
                .Select(grp => new MonthlyRow(grp.Key,
                    grp.Sum(t => t.NetProfit), grp.Count(),
                    grp.Count() > 0 ? 100.0 * grp.Count(t => t.NetProfit > 0) / grp.Count() : 0))
                .ToList();

            // By account for the range.
            var byAccount = trades.GroupBy(t => t.AccountId)
                .Select(grp => new ByAccountRow(AccName(grp.Key), grp.Sum(t => t.NetProfit), grp.Count()))
                .OrderByDescending(r => r.Net).ToList();

            var generatedAt = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var rangeText = (from, to) switch
            {
                (null, null) => "All time",
                ({ } f, null) => $"{f:yyyy-MM-dd} → …",
                (null, { } t) => $"… → {t:yyyy-MM-dd}",
                ({ } f, { } t) => $"{f:yyyy-MM-dd} → {t:yyyy-MM-dd}",
            };
            var accountsText = ids.Length == 0 ? "All accounts"
                : string.Join(", ", accounts.Select(a => AccName(a.Id)));

            var bytes = BuildReportPdf(generatedAt, rangeText, accountsText,
                netPl, tradeCount, winRate, standing, monthly, byAccount);
            return Results.File(bytes, "application/pdf", "profit-hub-report.pdf");
        });

        // Excel workbook (ClosedXML): Trades + Summary worksheets.
        g.MapGet("/workbook.xlsx", async (ClaimsPrincipal user, AppDbContext db,
            string? accountIds, DateTime? from, DateTime? to, long? magic, string period = "day") =>
        {
            if (!Reports.TryParseAccountIds(accountIds, out var ids))
                return Results.BadRequest(new { error = "invalid accountIds" });
            if (!Reports.IsValidPeriod(period))
                return Results.BadRequest(new { error = "period must be day, week or month" });
            var tz = Reports.Tz(user);
            var accounts = await db.Accounts.Where(a => a.UserId == user.UserId())
                .ToDictionaryAsync(a => a.Id, a => a.Name);
            var trades = await Reports.Filtered(db, user, ids, from, to, magic, tz)
                .OrderBy(t => t.CloseTimeUtc).ToListAsync();

            var bytes = BuildWorkbook(trades, accounts, tz, period);
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "profit-hub.xlsx");
        });
    }

    private record StandingRow(string Name, decimal NetDeposits, decimal Balance, decimal? Roi);
    private record MonthlyRow(DateOnly PeriodStart, decimal Net, int Trades, double WinRate);
    private record ByAccountRow(string Name, decimal Net, int Trades);

    private static readonly string Green = Colors.Green.Darken2;
    private static readonly string Red = Colors.Red.Darken2;
    private static string PlColor(decimal v) => v >= 0 ? Green : Red;

    private static byte[] BuildReportPdf(
        DateTime generatedAt, string rangeText, string accountsText,
        decimal netPl, int tradeCount, double winRate,
        IReadOnlyList<StandingRow> standing,
        IReadOnlyList<MonthlyRow> monthly,
        IReadOnlyList<ByAccountRow> byAccount)
    {
        var inv = CultureInfo.InvariantCulture;
        string Money(decimal v) => v.ToString("N2", inv);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Colors.Black));

                page.Header().Column(col =>
                {
                    col.Item().Text("Profit Hub — Trading Report")
                        .FontSize(18).Bold().FontColor(Colors.Black);
                    col.Item().PaddingTop(2).Text(t =>
                    {
                        t.Span("Generated ").FontColor(Colors.Grey.Darken1);
                        t.Span(generatedAt.ToString("yyyy-MM-dd HH:mm", inv)).FontColor(Colors.Grey.Darken1);
                    });
                    col.Item().Text(t =>
                    {
                        t.Span("Range: ").SemiBold();
                        t.Span(rangeText);
                        t.Span("    Accounts: ").SemiBold();
                        t.Span(accountsText);
                    });
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(14);

                    // KPI row
                    col.Item().Row(row =>
                    {
                        void Kpi(string label, string value, string color)
                        {
                            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2)
                                .Padding(8).Column(c =>
                                {
                                    c.Item().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                                    c.Item().PaddingTop(2).Text(value).FontSize(14).Bold().FontColor(color);
                                });
                        }
                        Kpi("Net P/L", Money(netPl), PlColor(netPl));
                        row.ConstantItem(8);
                        Kpi("Trades", tradeCount.ToString(inv), Colors.Black);
                        row.ConstantItem(8);
                        Kpi("Win rate", winRate.ToString("0.0", inv) + "%", Colors.Black);
                    });

                    // Account standing (lifetime)
                    col.Item().Text("Account standing (lifetime)").FontSize(11).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                        HeaderCell(table, "Account"); HeaderCell(table, "Net Deposits", true);
                        HeaderCell(table, "Balance", true); HeaderCell(table, "ROI %", true);
                        foreach (var r in standing)
                        {
                            BodyCell(table, r.Name);
                            BodyCell(table, Money(r.NetDeposits), true);
                            BodyCell(table, Money(r.Balance), true);
                            BodyCell(table, r.Roi is { } roi ? roi.ToString("0.00", inv) : "—", true);
                        }
                        if (standing.Count == 0) EmptyRow(table, 4);
                    });

                    // Monthly P/L
                    col.Item().Text("Monthly P/L").FontSize(11).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2); });
                        HeaderCell(table, "Month"); HeaderCell(table, "Net", true);
                        HeaderCell(table, "Trades", true); HeaderCell(table, "Win %", true);
                        foreach (var r in monthly)
                        {
                            BodyCell(table, r.PeriodStart.ToString("yyyy-MM", inv));
                            BodyCellColored(table, Money(r.Net), PlColor(r.Net));
                            BodyCell(table, r.Trades.ToString(inv), true);
                            BodyCell(table, r.WinRate.ToString("0.0", inv) + "%", true);
                        }
                        if (monthly.Count == 0) EmptyRow(table, 4);
                    });

                    // By account (range)
                    col.Item().Text("By Account").FontSize(11).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(4); c.RelativeColumn(2); c.RelativeColumn(2); });
                        HeaderCell(table, "Account"); HeaderCell(table, "Net", true); HeaderCell(table, "Trades", true);
                        foreach (var r in byAccount)
                        {
                            BodyCell(table, r.Name);
                            BodyCellColored(table, Money(r.Net), PlColor(r.Net));
                            BodyCell(table, r.Trades.ToString(inv), true);
                        }
                        if (byAccount.Count == 0) EmptyRow(table, 3);
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void HeaderCell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(6);
        var t = cell.Text(text).SemiBold().FontSize(8);
        if (right) t.AlignRight();
    }

    private static void BodyCell(TableDescriptor table, string text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3).PaddingHorizontal(6);
        var t = cell.Text(text).FontSize(8);
        if (right) t.AlignRight();
    }

    private static void BodyCellColored(TableDescriptor table, string text, string color)
    {
        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(3).PaddingHorizontal(6)
            .Text(text).FontSize(8).FontColor(color).AlignRight();
    }

    private static void EmptyRow(TableDescriptor table, int span)
    {
        table.Cell().ColumnSpan((uint)span).PaddingVertical(6)
            .Text("No data for this range.").FontSize(8).FontColor(Colors.Grey.Darken1).Italic();
    }

    private static byte[] BuildWorkbook(
        IReadOnlyList<Trade> trades, IReadOnlyDictionary<Guid, string> accounts,
        TimeZoneInfo tz, string period)
    {
        using var wb = new XLWorkbook();

        // ── Trades sheet ──────────────────────────────────────────────────────
        var ws = wb.Worksheets.Add("Trades");
        string[] headers =
        [
            "CloseTime(Local)", "Account", "Symbol", "Direction", "Lots", "OpenPrice",
            "ClosePrice", "GrossProfit", "Commission", "Swap", "NetProfit", "MagicNumber", "Comment"
        ];
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var t in trades)
        {
            ws.Cell(r, 1).Value = TimeZoneInfo.ConvertTimeFromUtc(t.CloseTimeUtc, tz);
            ws.Cell(r, 1).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
            ws.Cell(r, 2).Value = accounts.GetValueOrDefault(t.AccountId, "");
            ws.Cell(r, 3).Value = t.Symbol;
            ws.Cell(r, 4).Value = t.Direction;
            ws.Cell(r, 5).Value = t.Lots;
            ws.Cell(r, 6).Value = t.OpenPrice;
            ws.Cell(r, 7).Value = t.ClosePrice;
            ws.Cell(r, 8).Value = t.GrossProfit;
            ws.Cell(r, 9).Value = t.Commission;
            ws.Cell(r, 10).Value = t.Swap;
            ws.Cell(r, 11).Value = t.NetProfit;
            ws.Cell(r, 12).Value = t.MagicNumber;
            ws.Cell(r, 13).Value = t.Comment;
            r++;
        }
        // 2dp money formats on the numeric money columns.
        foreach (var col in new[] { 6, 7, 8, 9, 10, 11 })
            ws.Column(col).Style.NumberFormat.Format = "#,##0.00";
        // TOTAL row summing NetProfit.
        ws.Cell(r, 1).Value = "TOTAL";
        ws.Cell(r, 1).Style.Font.Bold = true;
        var totalCell = ws.Cell(r, 11);
        totalCell.FormulaA1 = trades.Count > 0 ? $"SUM(K2:K{r - 1})" : "0";
        totalCell.Style.Font.Bold = true;
        totalCell.Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();

        // ── Summary sheet ─────────────────────────────────────────────────────
        var ss = wb.Worksheets.Add("Summary");
        string[] sHeaders = ["PeriodStart", "NetProfit", "TradeCount", "Wins", "WinRate"];
        for (var c = 0; c < sHeaders.Length; c++)
            ss.Cell(1, c + 1).Value = sHeaders[c];
        ss.Row(1).Style.Font.Bold = true;
        ss.SheetView.FreezeRows(1);

        var sr = 2;
        var groups = trades
            .GroupBy(t => Reports.Bucket(t.CloseTimeUtc, tz, period))
            .OrderBy(grp => grp.Key);
        foreach (var grp in groups)
        {
            var count = grp.Count();
            var wins = grp.Count(t => t.NetProfit > 0);
            ss.Cell(sr, 1).Value = grp.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            ss.Cell(sr, 2).Value = grp.Sum(t => t.NetProfit);
            ss.Cell(sr, 3).Value = count;
            ss.Cell(sr, 4).Value = wins;
            ss.Cell(sr, 5).Value = count > 0 ? Math.Round(100.0 * wins / count, 1) : 0;
            sr++;
        }
        ss.Column(2).Style.NumberFormat.Format = "#,##0.00";
        ss.Column(5).Style.NumberFormat.Format = "0.0";
        ss.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static string Csv(string s) =>
        s.Contains(',') || s.Contains('"') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
