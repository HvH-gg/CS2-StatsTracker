using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using CSSharpUtils.Extensions;
using CSSharpUtils.Utils;
using HvH_StatsTracker.Models;
using Microsoft.Extensions.Logging;
using KeyValuePair = HvH_StatsTracker.Models.KeyValuePair;

namespace HvH_StatsTracker.Features;

public class EventCollector
{
    private bool _shouldCollect;
    private bool _hasMatchIdOverride;
    private readonly List<Event> _events = [];
    private int _roundEventId;
    private CCSGameRules? _gameRules;
    public Guid MatchId { get; private set; }

    private readonly ILogger<EventCollector> _logger;
    private readonly Networking _networking;
    
    public EventCollector(StatsTracker plugin, ILogger<EventCollector> logger, Networking networking)
    {
        _logger = logger;
        _networking = networking;
        MatchId = Guid.NewGuid();

        _logger.LogInformation("Registering event handlers");
        
        plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>(OnRoundAnnounceMatchStart);
        plugin.RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch);
        
        plugin.RegisterEventHandler<EventBotTakeover>(OnBotTakeover);
        
        plugin.RegisterEventHandler<EventBombBeginplant>(OnBombEvent);
        plugin.RegisterEventHandler<EventBombAbortplant>(OnBombEvent);
        plugin.RegisterEventHandler<EventBombPlanted>(OnBombEvent);
        plugin.RegisterEventHandler<EventBombDefused>(OnBombEvent);
        plugin.RegisterEventHandler<EventBombExploded>(OnBombEvent);
        
        plugin.RegisterEventHandler<EventHostageFollows>(OnHostageEvent);
        plugin.RegisterEventHandler<EventHostageRescued>(OnHostageEvent);
        plugin.RegisterEventHandler<EventHostageStopsFollowing>(OnHostageEvent);
        
        plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        
        plugin.RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        plugin.RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        plugin.RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        
        plugin.RegisterEventHandler<EventRoundMvp>(OnRoundMvp);
        plugin.RegisterEventHandler<EventRoundStart>(OnRoundStart);
        plugin.RegisterEventHandler<EventRoundEnd>(OnRoundEnd);
        
        plugin.RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        plugin.RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
    }

    public void SetMatchId(Guid matchId)
    {
        MatchId = matchId;
        _hasMatchIdOverride = true;
        _logger.LogInformation("Match ID overridden to {MatchId}", matchId);
    }

    private void StartCollecting()
    {
        _events.Clear();
        _shouldCollect = true;
        _roundEventId = 0;
        _logger.LogInformation("Started collecting events");
    }

    private void StopCollecting()
    {
        _shouldCollect = false;
        _logger.LogInformation("Stopped collecting events");
    }
    
    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent("player_disconnect", 
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "teamid", Value = e.Userid?.TeamNum.ToString() ?? "0" },
            new KeyValuePair { Key = "steamid", Value = e.Userid?.SteamID.ToString() ?? "0" },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        ));
        
        return HookResult.Continue;
    }
    
    private HookResult OnPlayerConnectFull(EventPlayerConnectFull e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent("player_connect", 
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "teamid", Value = e.Userid?.TeamNum.ToString() ?? "0" },
            new KeyValuePair { Key = "steamid", Value = e.Userid?.SteamID.ToString() ?? "0" }
        ));
        
        return HookResult.Continue;
    }
    
    private HookResult OnRoundAnnounceMatchStart(EventRoundAnnounceMatchStart e, GameEventInfo info)
    {
        if (!_hasMatchIdOverride)
        {
            MatchId = Guid.NewGuid();
        }

        StartCollecting();
        return HookResult.Continue;
    }

    private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch e, GameEventInfo info)
    {
        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "dummy", Value = "value" }
        ));
        StopCollecting();
        _networking.SubmitRoundData(_events, MatchId);
        _hasMatchIdOverride = false;
        return HookResult.Continue;
    }

    private HookResult OnBotTakeover(EventBotTakeover e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" }
        ));
        
        return HookResult.Continue;
    }

    private HookResult OnBombEvent(GameEvent e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var player = e.Get<CCSPlayerController>("userid");
        if (!player.IsPlayer())
            return HookResult.Continue;
        
        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "userid", Value = player.UserId.ToString() },
            new KeyValuePair { Key = "site", Value = e.Get<short>("site").ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnHostageEvent(GameEvent e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var player = e.Get<CCSPlayerController>("userid");
        if (!player.IsPlayer())
            return HookResult.Continue;
        
        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "userid", Value = player.UserId.ToString() },
            new KeyValuePair { Key = "hostage", Value = e.Get<short>("hostage").ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;
        
        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "weapon", Value = e.Weapon }
        ));
        
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" }
        ));
        
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventPlayerDeath = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "attacker", Value = e.Attacker?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "assister", Value = e.Assister?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "weapon", Value = e.Weapon },
            new KeyValuePair { Key = "headshot", Value = e.Headshot ? "true" : "false" }
        );

        if (e.Userid.IsPlayer())
            eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("userid", e.Userid!));
        
        if (e.Attacker.IsPlayer())
            eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("attacker", e.Attacker!));
        
        if (e.Assister.IsPlayer())
            eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("assister", e.Assister!));
        
        _events.Add(eventPlayerDeath);
        
        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventPlayerHurt = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "attacker", Value = e.Attacker?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "health", Value = e.Health.ToString() },
            new KeyValuePair { Key = "armor", Value = e.Armor.ToString() },
            new KeyValuePair { Key = "hitgroup", Value = e.Hitgroup.ToString() },
            new KeyValuePair { Key = "weapon", Value = e.Weapon },
            new KeyValuePair { Key = "dmg_health", Value = e.DmgHealth.ToString() }
        );

        if (e.Userid.IsPlayer())
            eventPlayerHurt.KvPairs.AddRange(GetPlayerPosition("userid", e.Userid!));
        
        if (e.Attacker.IsPlayer())
            eventPlayerHurt.KvPairs.AddRange(GetPlayerPosition("attacker", e.Attacker!));
        
        _events.Add(eventPlayerHurt);
        
        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventRoundMvp = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = e.Userid?.UserId.ToString() ?? "0" },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        );
        
        _events.Add(eventRoundMvp);
        
        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart e, GameEventInfo info)
    {
        // update game rules
        _gameRules = GameUtils.GetGameRules();
        
        _networking.SubmitRoundData(_events, MatchId);
        
        StartCollecting();
        return HookResult.Continue;
    }

    private HookResult OnRoundEnd(EventRoundEnd e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName, 
            new KeyValuePair { Key = "winner", Value = e.Winner.ToString() },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        ));
        
        StopCollecting();
        
        return HookResult.Continue;
    }
    
    private Event CreateEvent(string name, params KeyValuePair[] kvPairs)
    {
        var plantedBombEntity = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
        _gameRules ??= GameUtils.GetGameRules();
        
        return new Event
        {
            Id = _roundEventId++,
            Name = name,
            ServerTick = Server.TickCount,
            BombTimer = plantedBombEntity == null ? -1 : plantedBombEntity.C4Blow - Server.CurrentTime,
            BombDefuseTime = plantedBombEntity == null ? -1 : plantedBombEntity.DefuseCountDown - Server.CurrentTime,
            IsWarmup = _gameRules?.WarmupPeriod ?? true,
            KvPairs = kvPairs.ToList()
        };
    }
    
    private static List<KeyValuePair> GetPlayerPosition(string name, CCSPlayerController player)
    {
        var origin = player.AbsOrigin;
        if (origin == null)
            return [];
        
        return
        [
            new KeyValuePair { Key = $"{name}_x", Value = origin.X.ToString(CultureInfo.InvariantCulture) },
            new KeyValuePair { Key = $"{name}_y", Value = origin.Y.ToString(CultureInfo.InvariantCulture) },
            new KeyValuePair { Key = $"{name}_z", Value = origin.Z.ToString(CultureInfo.InvariantCulture) }
        ];
    }
}
