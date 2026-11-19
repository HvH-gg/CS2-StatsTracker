using System.Text.Json.Serialization;
using AsyncAwaitBestPractices;
using CounterStrikeSharp.API.Core;
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
    public override string ModuleVersion => "1.7.2";
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
        _serviceProvider.GetRequiredService<EventCollector>();
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