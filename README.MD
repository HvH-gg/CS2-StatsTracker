![Copyright ev0lve Digital](https://img.shields.io/badge/Copyright-ev0lve%20Digital-blue)

# [HvH.gg](https://hvh.gg) CS2 Stats Tracker (1.0.2)

Our CS2 Stats tracker plugin enables stats tracking for all players on a server and allows the server to participate in "Operation Winter-VACation".

# Requirements
- [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)
- [CounterStrikeSharp(v282)](https://github.com/roflmuffin/CounterStrikeSharp/releases)

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
   
   // do not change these
   "APIUrl": "https://hvh.gg/api/",
   "ConfigVersion": 1
}
```

# Credits
- [HvH.gg](https://hvh.gg)
- [Metamod:Source](https://www.sourcemm.net/)
- [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp)
