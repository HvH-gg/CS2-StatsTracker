using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace CS2_StatsTracker.Models;

public class KeyValuePair
{
    [Required] [JsonPropertyName("key")] public string Key { get; init; } = null!;

    [JsonPropertyName("value")] public string? Value { get; init; } = null!;

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
        {
            return false;
        }

        return Equals((KeyValuePair)obj);
    }

    private bool Equals(KeyValuePair other)
    {
        return Key == other.Key && Value == other.Value;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Key, Value);
    }
}

public class Event
{
    [Required] 
    [JsonPropertyName("id")] 
    public int Id { get; init; }
    
    [Required] 
    [JsonPropertyName("name")] 
    public string Name { get; init; } = null!;

    [Required]
    [JsonPropertyName("serverTick")]
    public int ServerTick { get; init; }
    
    [Required]
    [JsonPropertyName("isWarmup")]
    public bool IsWarmup { get; init; }

    [Required]
    [JsonPropertyName("bombTimer")]
    public float BombTimer { get; init; } = -1;

    [Required]
    [JsonPropertyName("bombDefuseTime")]
    public float BombDefuseTime { get; init; } = -1;

    [Required]
    [JsonPropertyName("kvPairs")]
    public List<KeyValuePair> KvPairs { get; init; } = [];

    public override bool Equals(object? obj)
    {
        if (obj == null || GetType() != obj.GetType())
            return false;

        return Equals((Event)obj);
    }

    private bool Equals(Event other)
    {
        var dataEquals = KvPairs.Count == other.KvPairs.Count;
        var failedKvPairs = KvPairs.Where(pair =>
            !pair.Key.EndsWith("_x") && !pair.Key.EndsWith("_y") && !pair.Key.EndsWith("_z") &&
            !other.KvPairs.Any(p => p.Key == pair.Key && p.Value == pair.Value)).ToList();
        if (failedKvPairs.Count != 0)
            dataEquals = false;

        return Id == other.Id && Name == other.Name && dataEquals;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name, KvPairs);
    }
}

public class ServerPlayer
{
    [Required]
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [Required]
    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = null!;

    [Required]
    [JsonPropertyName("teamId")]
    public int TeamId { get; set; }

    [Required]
    [JsonPropertyName("cheatId")]
    public int CheatId { get; set; } = 0;

    [JsonPropertyName("isBot")]
    public bool IsBot { get; set; }
}


public class RoundCombatPlayer
{
    [JsonPropertyName("steamId")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("assists")]
    public int? Assists { get; set; }

    [JsonPropertyName("damage")]
    public int? Damage { get; set; }

    [JsonPropertyName("utilityDamage")]
    public int? UtilityDamage { get; set; }

    [JsonPropertyName("deaths")]
    public int? Deaths { get; set; }

    [JsonPropertyName("headshotKills")]
    public int? HeadshotKills { get; set; }

    [JsonPropertyName("kills")]
    public int? Kills { get; set; }

    [JsonPropertyName("mvps")]
    public int? Mvps { get; set; }

    [JsonPropertyName("score")]
    public int? Score { get; set; }
}

public class RoundInput
{
    [Required]
    [JsonPropertyName("serverIp")]
    public string ServerIp { get; set; } = null!;

    [Required] 
    [JsonPropertyName("game")] 
    public int Game { get; set; }

    [Required]
    [JsonPropertyName("software")]
    public int Software { get; set; }

    [Required] 
    [JsonPropertyName("map")] 
    public string Map { get; set; } = null!;

    [Required]
    [JsonPropertyName("roundNumber")]
    public int RoundNumber { get; set; }
    
    [Required]
    [JsonPropertyName("ctScore")]
    public int CtScore { get; set; }
    
    [Required]
    [JsonPropertyName("tScore")]
    public int TScore { get; set; }

    [Required]
    [JsonPropertyName("combat")]
    public List<RoundCombatPlayer> Combat { get; set; } = [];
    
    [Required]
    [JsonPropertyName("players")]
    public List<ServerPlayer> Players { get; set; } = [];

    [Required]
    [JsonPropertyName("events")]
    public List<Event> Events { get; set; } = [];

    [Required]
    [JsonPropertyName("senderSteamId")]
    public string SenderSteamId { get; set; } = null!;
    
    [Required]
    [JsonPropertyName("matchId")]
    public string MatchId { get; set; } = null!;
    
    [Required]
    [JsonPropertyName("cvars")]
    public List<KeyValuePair> Cvars { get; set; } = [];
}