namespace ProfitHub.Api.Features;

public static class Endpoints
{
    public static void MapAll(WebApplication app)
    {
        Auth.Map(app);
    }
}
