using System.Text;
using System.Text.Json;
using AsyncAwaitBestPractices;
using CS2_StatsTracker.Models;
using Microsoft.Extensions.Logging;

namespace CS2_StatsTracker.Features;

public class Networking
{
    private readonly StatsTracker _plugin;
    private readonly ILogger<Networking> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NetworkRoundFactory _networkRoundFactory;


    public Networking(StatsTracker plugin, ILogger<Networking> logger, IHttpClientFactory httpClientFactory, NetworkRoundFactory networkRoundFactory)
    {
        _plugin = plugin;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _networkRoundFactory = networkRoundFactory;
    }

    public void SubmitRoundData(List<Event> events, Guid matchId)
    {
        if (events.Count == 0)
            return;

        _networkRoundFactory.PrepareRound(matchId);
        var roundInput = _networkRoundFactory.GetCurrentRoundInput(matchId);
        roundInput.Events = events;

        _logger.LogInformation("Sending round data for match {MatchId}", matchId);

        // serialized right away, the caller clears the event list afterwards
        Post(JsonSerializer.Serialize(roundInput)).SafeFireAndForget();
    }

    // Snapshot of the players the next round is emulated with, taken at round_start.
    public void PrepareNextRound(Guid matchId)
    {
        _networkRoundFactory.CreateRound(matchId);
    }

    private async Task<string> Post(string data)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", _plugin.Config.ApiKey);
        var response = await client.PostAsync(_plugin.Config.ApiUrl + "stats/log/server",
            new StringContent(data, Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
            return await response.Content.ReadAsStringAsync();

        _logger.LogWarning("API request failed: {StatusCode} {ReasonPhrase} {Body}", response.StatusCode, response.ReasonPhrase, await response.Content.ReadAsStringAsync());
        return "";
    }
}
