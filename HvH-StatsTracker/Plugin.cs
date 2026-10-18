using System.Text.Json.Serialization;
using AsyncAwaitBestPractices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Capabilities;
using CSSharpUtils.Extensions;
using HvH_StatsTracker.Features;
using HvH_StatsTracker.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HvH_StatsTracker;

public class StatsTracker : BasePlugin, IPluginConfig<Cs2StatsTrackerConfig>
{
    public override string ModuleName => "HvH.gg - Stats Tracker";
    public override string ModuleVersion => "1.3.0";
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
        _serviceProvider.GetRequiredService<Networking>();
        _serviceProvider.GetRequiredService<NetworkRoundFactory>();
        _serviceProvider.GetRequiredService<MatchManager>();
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
    [JsonPropertyName("ConfigVersion")] public override int Version { get; set; } = 1;
}