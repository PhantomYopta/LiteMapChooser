# LiteMapChooser
This plugin allows you to change map, nominate map, rtv

# Installation
1. Install [CounterStrike Sharp](https://github.com/roflmuffin/CounterStrikeSharp) and [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master)

2. Download LiteMapChooser

3. Unzip the archive and upload it to the game server

# Configs
The config is created automatically in the same place where the dll is located
```
{
  "RoundsBeforeNomination": 6,	//After how many rounds is given the opportunity to nominate a map
  "MaxRounds": 26,	//mp_maxrounds
  "VotingRoundInterval": 5,	//How many rounds after which to start voting on a completed map
  "Needed": 0.6 //Percentage of players needed to rockthevote
}
```

# Commands
**ccs_rtv**,**!rtv**- starts the map change process
**css_nominate**,**!nominate** - opens the map menu
