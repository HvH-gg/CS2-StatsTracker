using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using CSSharpUtils.Utils;
using HvH_StatsTracker.Models;
using Microsoft.Extensions.Logging;
using KeyValuePair = HvH_StatsTracker.Models.KeyValuePair;

namespace HvH_StatsTracker.Features;

public class NetworkRoundFactory
{
    private readonly ILogger<NetworkRoundFactory> _logger;
    private ConVar? _gameMode;
    private ConVar? _gameType;
    private ConVar? _teamMatesAreEnemies;
    private ConVar? _hostPort;
    private static RoundInput? _currentRoundInput = null;

    public NetworkRoundFactory(ILogger<NetworkRoundFactory> logger)
    {
        _logger = logger;
    }
    
    public RoundInput GetCurrentRoundInput(Guid matchId)
    {
        return _currentRoundInput ?? CreateRound(matchId);
    }
    
    public RoundInput CreateRound(Guid matchId)
    {
        _gameMode ??= ConVar.Find("game_mode");
        _gameType ??= ConVar.Find("game_type");
        _hostPort ??= ConVar.Find("hostport");
        _teamMatesAreEnemies ??= ConVar.Find("mp_teammates_are_enemies");
        
        var gameRules = GameUtils.GetGameRules();
        var roundNumber = gameRules?.TotalRoundsPlayed ?? 0;
        
        _logger.LogInformation("Creating round input for round {RoundNumber} in match {MatchId}", roundNumber, matchId);

        _currentRoundInput = new RoundInput
        {
            ServerIp = $"{ServerUtils.GetServerIp()}:{_hostPort?.GetPrimitiveValue<int>() ?? 0}",
            Events = [],
            Game = 1,
            Software = 0,
            RoundNumber = roundNumber,
            MatchId = matchId.ToString(),
            Map = Server.MapName,
            SenderSteamId = "0",
            Cvars =
            [
                new KeyValuePair { Key = "game_mode", Value = (_gameMode?.GetPrimitiveValue<int>() ?? 0).ToString() },
                new KeyValuePair { Key = "game_type", Value = (_gameType?.GetPrimitiveValue<int>() ?? 0).ToString() },
                new KeyValuePair { Key = "mp_teammates_are_enemies", Value = (_teamMatesAreEnemies?.GetPrimitiveValue<bool>() ?? false).ToString() }
            ],
            Players = Utilities.GetPlayers().Select(player => new ServerPlayer
            {
                UserId = player.UserId ?? 0,
                SteamId = player.SteamID.ToString(),
                TeamId = player.TeamNum,
                CheatId = 0,
                MatchId = matchId.ToString()
            }).ToList(),
        };
        
        return _currentRoundInput;
    }
}