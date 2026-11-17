using System.Net;
using System.Text;
using System.Text.Json;
using AsyncAwaitBestPractices;
using CS2_StatsTracker.Models;
using Microsoft.Extensions.Logging;

namespace CS2_StatsTracker.Features;

/// <summary>
/// Sends the rounds in the order they were played. A round that cannot be delivered (the API down, a timeout) stays at
/// the head of the queue and is tried again with a growing pause, the later rounds wait behind it so they arrive in
/// order. On unload the queue is written to disk and sent after the next load. The API drops a round it already
/// counted, so one sent twice (a timeout after it arrived) counts once.
/// </summary>
public class Networking
{
    private const string PendingFileName = "pending_rounds.json";
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly StatsTracker _plugin;
    private readonly ILogger<Networking> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NetworkRoundFactory _networkRoundFactory;

    private readonly object _lock = new();
    private readonly Queue<PendingRound> _queue = new();
    private bool _sending;
    private int _failures;
    private DateTime _nextAttemptUtc = DateTime.MinValue;

    private enum SendOutcome
    {
        Delivered,
        Rejected,
        Failed
    }

    public Networking(StatsTracker plugin, ILogger<Networking> logger, IHttpClientFactory httpClientFactory, NetworkRoundFactory networkRoundFactory)
    {
        _plugin = plugin;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _networkRoundFactory = networkRoundFactory;
    }

    /// <param name="segment">the number of a deathmatch piece, null for a round</param>
    public void SubmitRoundData(List<Event> events, Guid matchId, int? segment)
    {
        if (events.Count == 0)
            return;

        _networkRoundFactory.PrepareRound(matchId);
        var roundInput = _networkRoundFactory.GetCurrentRoundInput(matchId);
        roundInput.Events = events;
        roundInput.Segment = segment;

        _logger.LogInformation("Queueing round data for match {MatchId} (segment {Segment})", matchId, segment);

        // serialized right away, the caller clears the event list afterwards
        Enqueue(new PendingRound { Payload = JsonSerializer.Serialize(roundInput), CreatedAtUtc = DateTime.UtcNow });
        Pump();
    }

    // Snapshot of the players the next round is emulated with, taken at round_start.
    public void PrepareNextRound(Guid matchId)
    {
        _networkRoundFactory.CreateRound(matchId);
    }

    /// <summary>Sends the head of the queue unless a send is running or the pause after a failure is not over.</summary>
    public void Pump()
    {
        PendingRound next;
        lock (_lock)
        {
            if (_sending || DateTime.UtcNow < _nextAttemptUtc)
                return;

            DropExpired();
            if (_queue.Count == 0)
                return;

            next = _queue.Peek();
            _sending = true;
        }

        Send(next).SafeFireAndForget();
    }

    /// <summary>Writes what is still queued to disk, the next load sends it.</summary>
    public void SaveQueue()
    {
        List<PendingRound> pending;
        lock (_lock)
            pending = _queue.ToList();

        var path = Path.Combine(_plugin.ModuleDirectory, PendingFileName);
        try
        {
            if (pending.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            File.WriteAllText(path, JsonSerializer.Serialize(pending));
            _logger.LogInformation("Saved {Count} unsent rounds", pending.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not save {Count} unsent rounds", pending.Count);
        }
    }

    /// <summary>Queues the rounds the last unload left on disk, ahead of anything new.</summary>
    public void LoadQueue()
    {
        var path = Path.Combine(_plugin.ModuleDirectory, PendingFileName);
        if (!File.Exists(path))
            return;

        try
        {
            var pending = JsonSerializer.Deserialize<List<PendingRound>>(File.ReadAllText(path)) ?? [];
            File.Delete(path);
            lock (_lock)
            {
                var newer = _queue.ToList();
                _queue.Clear();
                foreach (var round in pending.Concat(newer))
                    _queue.Enqueue(round);
                TrimToSize();
            }

            _logger.LogInformation("Loaded {Count} unsent rounds", pending.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load the unsent rounds from {Path}", path);
        }

        Pump();
    }

    private void Enqueue(PendingRound round)
    {
        lock (_lock)
        {
            _queue.Enqueue(round);
            TrimToSize();
        }
    }

    // callers hold the lock
    private void TrimToSize()
    {
        var max = Math.Max(1, _plugin.Config.RetryQueueSize);
        while (_queue.Count > max)
        {
            _queue.Dequeue();
            _logger.LogWarning("Unsent rounds queue is full ({Max}), dropped the oldest round", max);
        }
    }

    // callers hold the lock; the API forgets which rounds it counted after a few hours, a round older than that could
    // count twice
    private void DropExpired()
    {
        var oldest = DateTime.UtcNow - TimeSpan.FromMinutes(Math.Max(1, _plugin.Config.RetryMaxAgeMinutes));
        while (_queue.Count > 0 && _queue.Peek().CreatedAtUtc < oldest)
        {
            _queue.Dequeue();
            _logger.LogWarning("Dropped an unsent round older than {Minutes} minutes", _plugin.Config.RetryMaxAgeMinutes);
        }
    }

    private async Task Send(PendingRound round)
    {
        var outcome = SendOutcome.Failed;
        try
        {
            outcome = await Post(round.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("API request failed: {Message}", ex.Message);
        }

        lock (_lock)
        {
            _sending = false;
            if (outcome == SendOutcome.Failed)
            {
                _failures++;
                var maxDelay = TimeSpan.FromSeconds(Math.Max(FirstRetryDelay.TotalSeconds, _plugin.Config.RetryMaxDelaySeconds));
                var delay = TimeSpan.FromSeconds(Math.Min(maxDelay.TotalSeconds,
                    FirstRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(_failures - 1, 16))));
                _nextAttemptUtc = DateTime.UtcNow + delay;
                _logger.LogWarning("Round not delivered, {Count} queued, next try in {Seconds} s", _queue.Count, (int)delay.TotalSeconds);
                return;
            }

            // the queue may have been trimmed meanwhile
            if (_queue.Count > 0 && ReferenceEquals(_queue.Peek(), round))
                _queue.Dequeue();
            _failures = 0;
            _nextAttemptUtc = DateTime.MinValue;
        }

        // the next one in line right away
        Pump();
    }

    private async Task<SendOutcome> Post(string data)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Add("X-Api-Key", _plugin.Config.ApiKey);
        var response = await client.PostAsync(_plugin.Config.ApiUrl + "stats/log/server",
            new StringContent(data, Encoding.UTF8, "application/json"));

        if (response.IsSuccessStatusCode)
            return SendOutcome.Delivered;

        var body = await response.Content.ReadAsStringAsync();
        // the API refused this round (a wrong key, a malformed round): sending it again changes nothing
        var retry = (int)response.StatusCode >= 500 || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
        _logger.LogWarning("API request failed: {StatusCode} {ReasonPhrase} {Body}{Dropped}", response.StatusCode, response.ReasonPhrase, body,
            retry ? "" : " (round dropped)");
        return retry ? SendOutcome.Failed : SendOutcome.Rejected;
    }

    private class PendingRound
    {
        public string Payload { get; init; } = string.Empty;
        public DateTime CreatedAtUtc { get; init; }
    }
}
