namespace ProfitHub.Api.Tests;

public class HealthTests : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client;
    public HealthTests(ApiFactory f) => _client = f.CreateClient();
    [Fact]
    public async Task Health_returns_ok()
        => Assert.Equal("ok", await _client.GetStringAsync("/health"));
}
