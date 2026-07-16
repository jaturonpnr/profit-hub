namespace ProfitHub.Api.Features;

public static class Endpoints
{
    public static void MapAll(WebApplication app)
    {
        Auth.Map(app);
        AdminUsers.Map(app);
        Me.Map(app);
        Ingest.Map(app);
        Accounts.Map(app);
        Reports.Map(app);
        Analytics.Map(app);
        EaNames.Map(app);
        Export.Map(app);
        Fx.Map(app);
        Insights.Map(app);
        Backtests.Map(app);
        Executions.Map(app);
        Withdrawals.Map(app);
        InputLabels.Map(app);
    }
}
