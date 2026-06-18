namespace ProfitHub.Api.Features;

/// Pure performance metrics over a set of realized Trade Net Profits (see CONTEXT.md:
/// Profit Factor, Expectancy, Realized Drawdown). No DB, no time — caller supplies the
/// values, ordered by close time ascending where order matters (RealizedDrawdown/Sparkline).
public static class Metrics
{
    /// Gross winning profit / gross losing loss. null = ∞ (no losing trades). 0 if empty.
    public static decimal? ProfitFactor(IEnumerable<decimal> netProfits)
    {
        decimal gp = 0m, gl = 0m;
        var any = false;
        foreach (var n in netProfits)
        {
            any = true;
            if (n > 0) gp += n; else gl += -n;
        }
        if (!any) return 0m;
        if (gl == 0m) return null;
        return Math.Round(gp / gl, 2);
    }

    /// Average Net Profit per Trade. 0 if empty.
    public static decimal Expectancy(IReadOnlyCollection<decimal> netProfits) =>
        netProfits.Count == 0 ? 0m : Math.Round(netProfits.Sum() / netProfits.Count, 2);

    /// Deepest peak-to-trough drop of the cumulative Net Profit curve, as an amount and
    /// as a percentage of the running peak at that point. Input ordered by close ascending.
    public static (decimal Amount, decimal Pct) RealizedDrawdown(IEnumerable<decimal> netProfitsAscending)
    {
        decimal cum = 0m, peak = 0m, maxDd = 0m, maxDdPct = 0m;
        foreach (var n in netProfitsAscending)
        {
            cum += n;
            if (cum > peak) peak = cum;
            var dd = peak - cum;
            if (dd > maxDd)
            {
                maxDd = dd;
                maxDdPct = peak > 0 ? Math.Round(dd / peak * 100m, 2) : 0m;
            }
        }
        return (Math.Round(maxDd, 2), maxDdPct);
    }

    /// Cumulative running sum (ascending), downsampled to <= max points. Always keeps the last.
    public static IReadOnlyList<decimal> Sparkline(IReadOnlyList<decimal> netProfitsAscending, int max)
    {
        var cum = new List<decimal>(netProfitsAscending.Count);
        decimal run = 0m;
        foreach (var n in netProfitsAscending) { run += n; cum.Add(Math.Round(run, 2)); }
        if (cum.Count <= max) return cum;
        var stride = (int)Math.Ceiling(cum.Count / (double)max);
        var outp = new List<decimal>();
        for (var i = 0; i < cum.Count; i += stride) outp.Add(cum[i]);
        if (outp[^1] != cum[^1]) outp.Add(cum[^1]);
        return outp;
    }
}
