using System.Text.Json.Serialization;
using AsyncAwaitBestPractices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Core.Capabilities;
using CSSharpUtils.Extensions;
using CS2_StatsTracker.Features;
using CS2_StatsTracker.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CS2_StatsTracker;

public class StatsTracker : BasePlugin, IPluginConfig<Cs2StatsTrackerConfig>
{
    public override string ModuleName => "HvH.gg - Stats Tracker";
    public override string ModuleVersion => "1.9.1";
    public override string ModuleAuthor => "imi-tat0r";
    public override string ModuleDescription => "HvH.gg Stats Tracker for CS2";
    private const string ModuleShortName = "StatsTracker";
    
    private readonly IServiceProvider _serviceProvider;

    public void OnConfigParsed(Cs2StatsTrackerConfig config)
    {
        Config = config;
        config.Update();
    }

    public Cs2StatsTrackerConfig Config { get; set; } = new();

    // Reads the config file again without a reload of the plugin: the key, the retry settings, and hvhgg_deathmatch is
    // set to Deathmatch (here also to off: the file was changed to say so).
    [ConsoleCommand("hvhgg_reload_config", "HvH.gg Stats Tracker: read the config file again")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void ReloadConfigCommand(CCSPlayerController? player, CommandInfo command)
    {
        try
        {
            // (the folder's name is the name the config file goes by)
            OnConfigParsed(ConfigManager.Load<Cs2StatsTrackerConfig>(Path.GetFileName(ModuleDirectory)));
        }
        catch (Exception e)
        {
            command.ReplyToCommand($"[HvH.gg] config not reloaded: {e.Message}");
            return;
        }
        _serviceProvider.GetRequiredService<EventCollector>().Deathmatch.Value = Config.Deathmatch;
        command.ReplyToCommand($"[HvH.gg] config reloaded: deathmatch={(Config.Deathmatch ? 1 : 0)}");
    }
    
    private readonly ILogger<StatsTracker> _logger;
    private static PluginCapability<IMatchManager> MatchManagerCapability { get; } = new("stats_tracker:match_manager");

    public StatsTracker(ILogger<StatsTracker> logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        SafeFireAndForgetExtensions.SetDefaultExceptionHandling(exception =>
        {
            _logger.LogInformation("Exception in async method");
            _logger.LogError(exception, "{Ex}", exception.ToString());
        });
        
        _logger.LogInformation("{ModuleName} Loading Stats Tracker plugin", ModuleShortName);
        
        // resolve the services to instantiate them
        // with its hvhgg_deathmatch, which starts as the config says (on only: the variable may be set already)
        var collector = _serviceProvider.GetRequiredService<EventCollector>();
        RegisterFakeConVars(collector);
        if (Config.Deathmatch)
            collector.Deathmatch.Value = true;
        // rounds the last unload could not send go out first
        _serviceProvider.GetRequiredService<Networking>().LoadQueue();
        // hvhgg_match_id lives with the round it is sent in
        RegisterFakeConVars(_serviceProvider.GetRequiredService<NetworkRoundFactory>());
        // other plugins set the match id through stats_tracker:match_manager
        Capabilities.RegisterPluginCapability(MatchManagerCapability, () => _serviceProvider.GetRequiredService<IMatchManager>());
    }

    public override void Unload(bool hotReload)
    {
        // the running deathmatch piece and everything unsent are kept on disk for the next load
        _serviceProvider.GetRequiredService<EventCollector>().FlushOnUnload();
        _serviceProvider.GetRequiredService<Networking>().SaveQueue();
        base.Unload(hotReload);
    }
}

public class Cs2StatsTrackerServiceCollection : IPluginServiceCollection<StatsTracker>
{
    public void ConfigureServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton(this);
        serviceCollection.AddSingleton<EventCollector>();
        serviceCollection.AddSingleton<Networking>();
        serviceCollection.AddSingleton<NetworkRoundFactory>();
        serviceCollection.AddSingleton<IMatchManager, MatchManager>();
        
        serviceCollection.AddHttpClient();
        
        serviceCollection.AddLogging(options =>
        {
            options.AddConsole();
        });
    }
}

public class Cs2StatsTrackerConfig : BasePluginConfig
{
    [JsonPropertyName("APIKey")] public string ApiKey { get; set; } = "YOUR_API_KEY";
    [JsonPropertyName("APIUrl")] public string ApiUrl { get; set; } = "https://hvh.gg/api/";

    // true on a server whose deathmatch is made by a plugin (game_type and game_mode are not deathmatch there, so it
    // is not detected); a server on the game's own deathmatch needs nothing set. What hvhgg_deathmatch starts as.
    [JsonPropertyName("Deathmatch")] public bool Deathmatch { get; set; } = false;

    // a deathmatch is sent in pieces of this many seconds (at least 10)
    [JsonPropertyName("DeathmatchSegmentSeconds")] public int DeathmatchSegmentSeconds { get; set; } = 60;

    // unsent rounds kept for retrying; past the size the oldest goes, past the age a round is dropped (the API stops
    // recognising a round it already counted after 6 hours)
    [JsonPropertyName("RetryQueueSize")] public int RetryQueueSize { get; set; } = 500;
    [JsonPropertyName("RetryMaxAgeMinutes")] public int RetryMaxAgeMinutes { get; set; } = 300;

    // the pause between tries grows from 5 seconds up to this
    [JsonPropertyName("RetryMaxDelaySeconds")] public int RetryMaxDelaySeconds { get; set; } = 300;

    [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 2;
}