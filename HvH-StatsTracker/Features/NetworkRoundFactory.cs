using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
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
    private ConVar? _sessionId;
    private static RoundInput? _currentRoundInput;

    public NetworkRoundFactory(ILogger<NetworkRoundFactory> logger)
    {
        _logger = logger;
    }
    
    public void PrepareRound(Guid matchId)
    {
        if (_currentRoundInput == null)
            return;

        if (_currentRoundInput.MatchId != matchId.ToString())
        {
            _logger.LogInformation("Match ID changed, canceling round preparation");
            return;
        }
        
        var gameRules = GameUtils.GetGameRules();
        var teams = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager").ToList();

        var terrorists = teams.First(team => (CsTeam)team.TeamNum == CsTeam.Terrorist);
        var cts = teams.First(team => (CsTeam)team.TeamNum == CsTeam.CounterTerrorist);

        _currentRoundInput.Combat = Utilities.GetPlayers().Select(player => new RoundCombatPlayer
        {
            SteamId = player.SteamID.ToString(),
            Kills = player.ActionTrackingServices?.MatchStats.Kills,
            Deaths = player.ActionTrackingServices?.MatchStats.Deaths,
            Assists = player.ActionTrackingServices?.MatchStats.Assists,
            Damage = player.ActionTrackingServices?.MatchStats.Damage,
            UtilityDamage = player.ActionTrackingServices?.MatchStats.UtilityDamage,
            HeadshotKills = player.ActionTrackingServices?.MatchStats.HeadShotKills,
            FirstKills = 0,
            Mvps = player.MVPs,
            Score = player.Score,
        }).ToList();

        _currentRoundInput.RoundNumber = gameRules?.TotalRoundsPlayed ?? 0;
        _currentRoundInput.CtScore = cts.Score;
        _currentRoundInput.TScore = terrorists.Score;
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
        _sessionId ??= ConVar.Find("steamworks_sessionid_server");
        _teamMatesAreEnemies ??= ConVar.Find("mp_teammates_are_enemies");
        
        _currentRoundInput = new RoundInput
        {
            ServerIp = $"{ServerUtils.GetServerIp()}:{_hostPort?.GetPrimitiveValue<int>() ?? 0}",
            Events = [],
            Game = 1,
            Software = 0,
            MatchId = matchId.ToString(),
            Map = Server.MapName,
            SenderSteamId = "0",
            Cvars =
            [
                new KeyValuePair { Key = "game_mode", Value = (_gameMode?.GetPrimitiveValue<int>() ?? 0).ToString() },
                new KeyValuePair { Key = "game_type", Value = (_gameType?.GetPrimitiveValue<int>() ?? 0).ToString() },
                new KeyValuePair { Key = "mp_teammates_are_enemies", Value = (_teamMatesAreEnemies?.GetPrimitiveValue<bool>() ?? false ? 1 : 0).ToString() },
                new KeyValuePair { Key = "steamworks_sessionid_server", Value = _sessionId?.StringValue ?? "0" }
            ],
            Players = Utilities.GetPlayers().Select(player => new ServerPlayer
            {
                UserId = player.UserId ?? 0,
                SteamId = player.SteamID.ToString(),
                TeamId = player.TeamNum,
                CheatId = 0,
                MatchId = matchId.ToString()
            }).ToList()
        };
        
        return _currentRoundInput;
    }
}