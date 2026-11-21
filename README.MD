![Copyright ev0lve Digital](https://img.shields.io/badge/Copyright-ev0lve%20Digital-blue)

# [HvH.gg](https://hvh.gg) CS2 Stats Tracker (1.8.0)

Our CS2 Stats tracker plugin enables stats tracking for all players on a server.

# What this plugin does
This is not a stats tracker for your own server. The plugin sends the rounds played on your server to [HvH.gg](https://hvh.gg), where they count towards the stats of every player on HvH.gg: profiles, rankings and match history. It adds no stats commands, menus or pages to your server itself.

# Scout bot
Servers without this plugin are visited by the HvH.gg scout bot, which joins to read the scoreboard. While your server sends rounds with this plugin, the scout bot no longer joins your server, so it does not take up a player slot.

# Requirements
- [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) v1.0.376 or newer (.NET 10 runtime)

# Install
1. Install Metamod:Source and CounterStrikeSharp
2. Place the `addons` folder in your servers `game/csgo/` directory  
   ![extract](https://du.hurenso.hn/r/0NyFPY.png)
3. Add your API Key in the config file.  
   3.1. Located at `addons/counterstrikesharp/configs/plugins/CS2-StatsTracker/CS2-StatsTracker.json`
4. Restart your server

# Config
```json
{
   "APIKey": "YOUR_API_KEY",

   // true if a plugin makes your deathmatch (see below)
   "Deathmatch": false,
   
   // do not change these
   "APIUrl": "https://hvh.gg/api/",
   "DeathmatchSegmentSeconds": 60,
   "RetryQueueSize": 500,
   "RetryMaxAgeMinutes": 300,
   "RetryMaxDelaySeconds": 300,
   "ConfigVersion": 2
}
```

A deathmatch has no rounds, so it is sent every `DeathmatchSegmentSeconds`. The game's own deathmatch (`game_type 1`, `game_mode 2`) is detected. If a plugin makes your deathmatch on another game mode, set `"Deathmatch": true`, otherwise nothing is counted there. A server that runs it only at times sets `hvhgg_deathmatch 1` while it does (and `0` after), for example in the cfg of the mode. Rounds that cannot be sent (HvH.gg not reachable) are kept, sent again in order, and saved to disk when the plugin unloads.

# Match systems
If your match system has its own match ids, set `hvhgg_match_id "YOUR_MATCH_ID"` when a match starts and empty it (`hvhgg_match_id ""`) when it is over. Rounds are sent with it, so the match can be found by your id.

# Credits
- [HvH.gg](https://hvh.gg)
- [Metamod:Source](https://www.sourcemm.net/)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
