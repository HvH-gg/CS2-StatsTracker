using System.Globalization;
using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.UserMessages;
using CSSharpUtils.Utils;
using CS2_StatsTracker.Models;
using Microsoft.Extensions.Logging;
using KeyValuePair = CS2_StatsTracker.Models.KeyValuePair;

namespace CS2_StatsTracker.Features;

public class EventCollector
{
    // Disabled between cs_win_panel_match and the next match or map start, like the CSGO tracker.
    private bool _shouldCollect = true;
    private bool _hasMatchIdOverride;
    private readonly List<Event> _events = [];
    private int _roundEventId;
    private CCSGameRules? _gameRules;

    // Each player's shots; sent beside the events, not as one.
    private readonly Dictionary<string, List<ShotInfo>> _shots = new();

    // CMsgTEFireBullets
    private const int FireBulletsMessageId = 452;
    public Guid MatchId { get; private set; }

    // A deathmatch has one long round, so it is sent in pieces (segments) of DeathmatchSegmentSeconds instead of at
    // its end: a crash or map change loses at most one piece. Numbered per map from 1; null while buffering a round.
    private int _segmentCount;
    private int? _currentSegment;
    private float _segmentStartedAt;
    private ConVar? _gameType;
    private ConVar? _gameMode;

    private readonly StatsTracker _plugin;
    private readonly ILogger<EventCollector> _logger;
    private readonly Networking _networking;

    public EventCollector(StatsTracker plugin, ILogger<EventCollector> logger, Networking networking)
    {
        _plugin = plugin;
        _logger = logger;
        _networking = networking;
        MatchId = Guid.NewGuid();

        _logger.LogInformation("Registering event handlers");

        // closes deathmatch pieces and retries unsent rounds; runs across map changes
        plugin.AddTimer(1.0f, OnTick, TimerFlags.REPEAT);

        plugin.RegisterListener<Listeners.OnMapStart>(OnMapStart);
        plugin.RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

        plugin.RegisterEventHandler<EventRoundAnnounceMatchStart>(OnRoundAnnounceMatchStart);
        plugin.RegisterEventHandler<EventCsWinPanelMatch>(OnCsWinPanelMatch);

        plugin.RegisterEventHandler<EventBotTakeover>(OnBotTakeover);

        // Typed lambdas, not the shared GameEvent method group: CounterStrikeSharp builds the event object from the
        // handler method's parameter type, so a GameEvent parameter makes it hand a plain GameEvent to a typed delegate.
        plugin.RegisterEventHandler<EventBombBeginplant>((e, info) => OnBombEvent(e, info));
        plugin.RegisterEventHandler<EventBombAbortplant>((e, info) => OnBombEvent(e, info));
        plugin.RegisterEventHandler<EventBombPlanted>((e, info) => OnBombEvent(e, info));
        plugin.RegisterEventHandler<EventBombDefused>((e, info) => OnBombEvent(e, info));
        plugin.RegisterEventHandler<EventBombExploded>((e, info) => OnBombEvent(e, info));

        plugin.RegisterEventHandler<EventHostageFollows>((e, info) => OnHostageEvent(e, info));
        plugin.RegisterEventHandler<EventHostageRescued>((e, info) => OnHostageEvent(e, info));
        plugin.RegisterEventHandler<EventHostageStopsFollowing>((e, info) => OnHostageEvent(e, info));

        plugin.RegisterEventHandler<EventWeaponFire>(OnWeaponFire);
        plugin.HookUserMessage(FireBulletsMessageId, OnFireBullets);

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

    // Starts a new round buffer (or deathmatch piece). The backend needs round_start as the first event of every round.
    private void StartRound()
    {
        _events.Clear();
        _shots.Clear();
        _roundEventId = 0;
        _currentSegment = IsDeathmatch() ? ++_segmentCount : null;
        _segmentStartedAt = Server.CurrentTime;
        _events.Add(CreateEvent("round_start"));
    }

    // Sends the buffered round, the backend rejects rounds without round_end so those are dropped. A deathmatch piece
    // has no round_end until the match ends and is sent as long as anything happened in it.
    private void FlushRound()
    {
        if (_events.Any(e => e.Name == "round_end") ||
            (_currentSegment != null && _events.Any(e => e.Name != "round_start")))
            _networking.SubmitRoundData(_events,
                _shots.Select(s => new PlayerShotInfo { SteamId = s.Key, Shots = s.Value }).ToList(), MatchId, _currentSegment);

        _events.Clear();
        _shots.Clear();
        _roundEventId = 0;
        _currentSegment = null;
    }

    // The config's Deathmatch is what it starts as (Plugin.Load); a server that runs its deathmatch only at times sets
    // it in the cfg of the mode. HvH.gg Recast has the same variable and reads it too.
    public readonly FakeConVar<bool> Deathmatch = new("hvhgg_deathmatch",
        "1 while the server runs a deathmatch made by a plugin (not detected: game_type and game_mode are not deathmatch there); starts as Deathmatch in the config",
        false);

    // The game's own deathmatch (game_type 1, game_mode 2), or hvhgg_deathmatch says the server runs one (a plugin's).
    private bool IsDeathmatch()
    {
        if (Deathmatch.Value)
            return true;
        _gameType ??= ConVar.Find("game_type");
        _gameMode ??= ConVar.Find("game_mode");
        return _gameType?.GetPrimitiveValue<int>() == 1 && _gameMode?.GetPrimitiveValue<int>() == 2;
    }

    private void OnTick()
    {
        if (_shouldCollect && _currentSegment != null &&
            Server.CurrentTime - _segmentStartedAt >= Math.Max(10, _plugin.Config.DeathmatchSegmentSeconds))
        {
            // the piece so far goes out, the next one starts with the players as they are now
            FlushRound();
            _networking.PrepareNextRound(MatchId);
            StartRound();
        }
        else if (_shouldCollect && _currentSegment == null && IsDeathmatch() &&
                 !_events.Any(e => e.Name == "round_start"))
        {
            // loaded in the middle of a deathmatch: its only round_start is long gone, start a piece now
            _networking.PrepareNextRound(MatchId);
            StartRound();
        }

        _networking.Pump();
    }

    /// <summary>On unload: a deathmatch piece goes into the queue that is saved to disk (a half round would be dropped anyway).</summary>
    public void FlushOnUnload()
    {
        if (_currentSegment != null)
            FlushRound();
    }

    private void OnMapStart(string mapName)
    {
        _gameRules = null;
        _events.Clear();
        _shots.Clear();
        _roundEventId = 0;
        _shouldCollect = true;
        _segmentCount = 0;
        _currentSegment = null;
    }

    // the next round_start happens on another map, so flush here
    private void OnMapEnd()
    {
        FlushRound();
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect e, GameEventInfo info)
    {
        if (!_shouldCollect || e.Userid is not { IsValid: true, IsBot: false })
            return HookResult.Continue;

        _events.Add(CreateEvent("player_disconnect",
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "teamid", Value = e.Userid.TeamNum.ToString() },
            new KeyValuePair { Key = "steamid", Value = e.Userid.SteamID.ToString() },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull e, GameEventInfo info)
    {
        if (!_shouldCollect || e.Userid is not { IsValid: true, IsBot: false })
            return HookResult.Continue;

        _events.Add(CreateEvent("player_connect",
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "teamid", Value = e.Userid.TeamNum.ToString() },
            new KeyValuePair { Key = "steamid", Value = e.Userid.SteamID.ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnRoundAnnounceMatchStart(EventRoundAnnounceMatchStart e, GameEventInfo info)
    {
        if (!_hasMatchIdOverride)
        {
            MatchId = Guid.NewGuid();
        }

        _shouldCollect = true;
        _logger.LogInformation("Started collecting events");
        return HookResult.Continue;
    }

    private HookResult OnCsWinPanelMatch(EventCsWinPanelMatch e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName));

        // flush what is buffered, the next round_start may never arrive
        FlushRound();
        _shouldCollect = false;
        _hasMatchIdOverride = false;
        _logger.LogInformation("Stopped collecting events");
        return HookResult.Continue;
    }

    private HookResult OnBotTakeover(EventBotTakeover e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "botid", Value = UserRef(e.Botid) }
        ));

        return HookResult.Continue;
    }

    private HookResult OnBombEvent(GameEvent e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Get<CCSPlayerController>("userid")) },
            new KeyValuePair { Key = "site", Value = e.Get<short>("site").ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnHostageEvent(GameEvent e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Get<CCSPlayerController>("userid")) },
            new KeyValuePair { Key = "hostage", Value = e.Get<short>("hostage").ToString() }
        ));

        return HookResult.Continue;
    }

    private HookResult OnWeaponFire(EventWeaponFire e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "weapon", Value = StripWeaponPrefix(e.Weapon) }
        ));

        return HookResult.Continue;
    }

    // The game announces every shot with this message: where it was fired from and at which angles.
    private HookResult OnFireBullets(UserMessage message)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        try
        {
            // an entity handle: the low 15 bits are the entity index
            var pawn = Utilities.GetEntityFromIndex<CCSPlayerPawn>((int)(message.ReadUInt("player") & 0x7FFF));
            // JSON has no infinity or NaN
            if (pawn is { IsValid: true, DesignerName: "player" } &&
                pawn.Controller.Value?.As<CCSPlayerController>() is { IsValid: true, IsBot: false } player &&
                ReadShotInfo(message) is { } shot && shot.Origin.All(float.IsFinite) && shot.Angles.All(float.IsFinite))
            {
                var steamId = player.SteamID.ToString();
                if (!_shots.TryGetValue(steamId, out var shots))
                    _shots[steamId] = shots = [];
                shots.Add(shot);
            }
        }
        catch
        {
            // this shot is left out
        }

        return HookResult.Continue;
    }

    // CounterStrikeSharp reads no field of a message inside the message (origin and angles are such), so they are read
    // from the message's memory. The plugin's UserMessage starts with the pointer to the game's message
    // (CNetMessagePB); in it the protobuf message follows the 0x30 bytes of CNetMessage and has the pointers to its
    // origin at 0x18 and its angles at 0x20, each with x, y and z from 0x18.
    private const int ProtobufOffset = 0x30;
    private const int OriginOffset = 0x18;
    private const int AnglesOffset = 0x20;
    private const int VectorOffset = 0x18;

    // Before the first read is trusted it is compared with the message as text once: a game update that moves the
    // fields turns the reads off instead of sending wrong shots.
    private bool _shotReadChecked;
    private bool _shotReadOff;

    private ShotInfo? ReadShotInfo(UserMessage message)
    {
        if (_shotReadOff)
            return null;

        var netMessage = Marshal.ReadIntPtr(message.Handle);
        if (netMessage == IntPtr.Zero)
            return null;
        var shot = new ShotInfo
        {
            Origin = ReadVector(Marshal.ReadIntPtr(netMessage, ProtobufOffset + OriginOffset)),
            Angles = ReadVector(Marshal.ReadIntPtr(netMessage, ProtobufOffset + AnglesOffset))
        };
        if (_shotReadChecked)
            return shot;

        // a shot whose text has no number to compare with (inf, nan) is left to the next one
        if (FirstAngleFromText(message.DebugString) is not { } expected)
            return null;
        if (MathF.Abs(shot.Angles[0] - expected) > 1e-3f * MathF.Max(1, MathF.Abs(expected)))
        {
            _shotReadOff = true;
            _logger.LogWarning("Shot angles read {Read} but the message says {Expected}: shots are no longer sent",
                shot.Angles[0], expected);
            return null;
        }

        _shotReadChecked = true;
        return shot;
    }

    // x, y and z of a vector or angles message; all 0 when the message has none.
    private static float[] ReadVector(IntPtr vector)
    {
        if (vector == IntPtr.Zero)
            return [0, 0, 0];
        return
        [
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(vector, VectorOffset)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(vector, VectorOffset + 4)),
            BitConverter.Int32BitsToSingle(Marshal.ReadInt32(vector, VectorOffset + 8))
        ];
    }

    // The message as text: "angles {", then "x: <value>" among its lines; a value of 0 is left out there. Null when
    // the text has no angles or the value is no number.
    private static float? FirstAngleFromText(string text)
    {
        const string open = "angles {";
        var start = text.StartsWith(open, StringComparison.Ordinal) ? 0 : text.IndexOf("\n" + open, StringComparison.Ordinal);
        if (start < 0)
            return null;
        var end = text.IndexOf('}', start);
        if (end < 0)
            return null;

        var block = text.AsSpan(start, end - start);
        var x = block.IndexOf("x: ", StringComparison.Ordinal);
        if (x < 0)
            return 0;
        var value = block[(x + 3)..];
        var lineEnd = value.IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
            value = value[..lineEnd];
        return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) ? angle : null;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        // who the slot is and the team: a spectator joining a team isn't in the round's player list and sends no
        // connect, without these the backend can't name him
        _events.Add(e.Userid is { IsValid: true, IsBot: false }
            ? CreateEvent(e.EventName,
                new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
                new KeyValuePair { Key = "steamid", Value = e.Userid.SteamID.ToString() },
                new KeyValuePair { Key = "teamid", Value = e.Userid.TeamNum.ToString() })
            : CreateEvent(e.EventName,
                new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) }));

        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventPlayerDeath = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "attacker", Value = UserRef(e.Attacker) },
            new KeyValuePair { Key = "assister", Value = UserRef(e.Assister) },
            new KeyValuePair { Key = "weapon", Value = StripWeaponPrefix(e.Weapon) },
            new KeyValuePair { Key = "headshot", Value = e.Headshot ? "true" : "false" },
            new KeyValuePair { Key = "penetrated", Value = e.Penetrated > 0 ? "true" : "false" },
            new KeyValuePair { Key = "attackerinair", Value = e.Attackerinair ? "true" : "false" },
            new KeyValuePair { Key = "noscope", Value = e.Noscope ? "true" : "false" }
        );

        eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("userid", e.Userid));
        eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("attacker", e.Attacker));
        eventPlayerDeath.KvPairs.AddRange(GetPlayerPosition("assister", e.Assister));

        _events.Add(eventPlayerDeath);

        return HookResult.Continue;
    }

    private HookResult OnPlayerHurt(EventPlayerHurt e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventPlayerHurt = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "attacker", Value = UserRef(e.Attacker) },
            new KeyValuePair { Key = "health", Value = e.Health.ToString() },
            new KeyValuePair { Key = "armor", Value = e.Armor.ToString() },
            new KeyValuePair { Key = "hitgroup", Value = e.Hitgroup.ToString() },
            new KeyValuePair { Key = "weapon", Value = StripWeaponPrefix(e.Weapon) },
            new KeyValuePair { Key = "dmg_health", Value = e.DmgHealth.ToString() }
        );

        eventPlayerHurt.KvPairs.AddRange(GetPlayerPosition("userid", e.Userid));
        eventPlayerHurt.KvPairs.AddRange(GetPlayerPosition("attacker", e.Attacker));

        _events.Add(eventPlayerHurt);

        return HookResult.Continue;
    }

    private HookResult OnRoundMvp(EventRoundMvp e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        var eventRoundMvp = CreateEvent(e.EventName,
            new KeyValuePair { Key = "userid", Value = UserRef(e.Userid) },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        );

        _events.Add(eventRoundMvp);

        return HookResult.Continue;
    }

    private HookResult OnRoundStart(EventRoundStart e, GameEventInfo info)
    {
        // update game rules
        _gameRules = GameUtils.GetGameRules();

        if (!_shouldCollect)
            return HookResult.Continue;

        // flush the round that just finished before starting a new buffer
        FlushRound();
        _networking.PrepareNextRound(MatchId);

        StartRound();
        return HookResult.Continue;
    }

    // Collection keeps running after round_end, round_mvp and late events still belong to this round.
    private HookResult OnRoundEnd(EventRoundEnd e, GameEventInfo info)
    {
        if (!_shouldCollect)
            return HookResult.Continue;

        _events.Add(CreateEvent(e.EventName,
            new KeyValuePair { Key = "winner", Value = e.Winner.ToString() },
            new KeyValuePair { Key = "reason", Value = e.Reason.ToString() }
        ));

        return HookResult.Continue;
    }

    private Event CreateEvent(string name, params KeyValuePair[] kvPairs)
    {
        var plantedBombEntity = Utilities.FindAllEntitiesByDesignerName<CPlantedC4>("planted_c4").FirstOrDefault();
        _gameRules ??= GameUtils.GetGameRules();
        var now = Server.CurrentTime;

        return new Event
        {
            Id = _roundEventId++,
            Name = name,
            ServerTick = Server.TickCount,
            // seconds left until detonation / until the defuse finishes, -1 when there is none
            BombTimer = plantedBombEntity == null ? -1 : Math.Max(0, plantedBombEntity.C4Blow - now),
            BombDefuseTime = plantedBombEntity is { BeingDefused: true }
                ? Math.Max(0, plantedBombEntity.DefuseCountDown - now)
                : -1,
            IsWarmup = _gameRules?.WarmupPeriod ?? true,
            KvPairs = kvPairs.ToList()
        };
    }

    // Entity index, the same id the CSGO tracker sends (client index). 0 means nobody.
    private static string UserRef(CCSPlayerController? player)
    {
        return player is { IsValid: true } ? player.Index.ToString() : "0";
    }

    // The backend prepends "weapon_" itself, so the prefix has to be stripped here.
    private static string StripWeaponPrefix(string weapon)
    {
        return weapon.StartsWith("weapon_") ? weapon["weapon_".Length..] : weapon;
    }

    private static List<KeyValuePair> GetPlayerPosition(string name, CCSPlayerController? player)
    {
        if (player is not { IsValid: true })
            return [];

        var origin = player.PlayerPawn.Value?.AbsOrigin;
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
