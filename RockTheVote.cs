using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace RockTheVote;

public class RockTheVote : BasePlugin
{
    public override string ModuleName => "MapChooser by phantom";
    public override string ModuleVersion => "v1.0.1";

    private Config _config;
    private Dictionary<string, int> optionCounts = new Dictionary<string, int>();
    private Users?[] _usersArray = new Users?[65];
    private Timer? _mapTimer;
    
    private int _votedRtv;
    private int _votedMap;
    private int _countRounds;
    private float _timeLimit;

    private string? _selectedMap;
    private string[] _proposedMaps = new string[7];
    private List<string> _playedMaps = new List<string>();

    private bool _isVotingActive;
    private bool IsTimeLimit;
    private bool IsRoundLimit;

    public override void Load(bool hotReload)
    {
        _config = LoadConfig();

        string mapsFilePath = Path.Combine(ModuleDirectory, "maps.txt");

        if (!File.Exists(mapsFilePath))
            File.WriteAllText(mapsFilePath, "");

        RegisterEventHandler<EventRoundEnd>(EventRoundEnd);
        RegisterListener<Listeners.OnClientConnected>(slot =>
        {
            _usersArray[slot + 1] = new Users { ProposedMaps = null!, VotedRtv = false };
        });
        RegisterEventHandler<EventRoundStart>(((@event, info) =>
        {
            if (_mapTimer != null) return HookResult.Continue;
            
            IsTimeLimit = false;
            _timeLimit = ConVar.Find("mp_timelimit")!.GetPrimitiveValue<float>() * 60.0f;
            
            if (_timeLimit > 0 && _timeLimit - _config.VotingTimeInterval * 60.0f > 0)
            {
                _mapTimer = AddTimer(_timeLimit - _config.VotingTimeInterval,
                    () =>
                    {
                        IsTimeLimit = true;
                        VoteMap(false);
                    });
            }
            return HookResult.Continue;
        }));
        RegisterListener<Listeners.OnMapStart>(name =>
        {
            ResetData();
            _mapTimer = null;
            _countRounds = 0;
            _selectedMap = null;
            if (_playedMaps.Count >= _config.RoundsBeforeNomination)
                _playedMaps.RemoveAt(0);

            if (!_playedMaps.Contains(name))
                _playedMaps.Add(name);
        });
        RegisterListener<Listeners.OnClientDisconnectPost>(slot =>
        {
            if (_usersArray[slot + 1]!.VotedRtv)
                _votedRtv--;

            for (var index = 0; index < _proposedMaps.Length; index++)
            {
                if (_usersArray[slot + 1]!.ProposedMaps == _proposedMaps[index])
                    _proposedMaps[index] = null!;
            }

            _usersArray[slot + 1] = null!;
        });
        AddCommand("css_rtv", "", CommandRtv);
        AddCommand("css_nominate", "", ((player, info) =>
        {
            var mapsPath = Path.Combine(ModuleDirectory, "maps.txt");
            var mapList = File.ReadAllLines(mapsPath);
            var nominateMenu = new ChatMenu("Nominate");
            foreach (var map in mapList)
            {
                string mapName = map.Replace("ws:", "").Trim();
                nominateMenu.AddMenuOption(mapName, HandleNominate);
            }

            if (player == null) return;
            ChatMenus.OpenMenu(player, nominateMenu);
        }));
    }

    private HookResult EventRoundEnd(EventRoundEnd @event, GameEventInfo info)
    {
        _countRounds++;
        var maxrounds = ConVar.Find("mp_maxrounds").GetPrimitiveValue<int>();
        if (_countRounds == (maxrounds - _config.VotingRoundInterval))
        {
            VoteMap(false);
        }
        else if (_countRounds == maxrounds)
        {
            Server.ExecuteCommand(!IsWsMaps(_selectedMap)
                ? $"map {_selectedMap}"
                : $"ds_workshop_changelevel {_selectedMap}");
        }

        return HookResult.Continue;
    }

    private void CommandRtv(CCSPlayerController? player, CommandInfo commandinfo)
    {
        if (player == null) return;

        if (_selectedMap != null)
        {
            PrintToChat(player, "RTV has already ended, you cannot start it again or nominate maps.");
            return;
        }

        if (!_isVotingActive)
        {
            PrintToChat(player, "Rock the Vote is not allowed yet.");
            return;
        }

        var countPlayers = _usersArray.Count(user => user != null);
        var countVote = (int)(countPlayers * _config.Needed) == 0 ? 1 : countPlayers * _config.Needed;
        var user = _usersArray[player.EntityIndex!.Value.Value]!;
        if (user.VotedRtv)
        {
            PrintToChat(player, "You have already voted to change the map");
            return;
        }

        user.VotedRtv = true;
        _votedRtv++;
        PrintToChatAll($"{player.PlayerName} wants to rock the vote. ({_votedRtv} votes, {(int)countVote} required)");

        if (_votedRtv == (int)countVote)
            VoteMap(true);
    }

    void HandleNominate(CCSPlayerController player, ChatMenuOption option)
    {
        if (_selectedMap != null)
        {
            PrintToChat(player, "RTV has already ended, you cannot start it again or nominate maps.");
            return;
        }

        if (!_isVotingActive)
        {
            PrintToChat(player, "Rock the Vote is not allowed yet.");
            return;
        }

        var indexToAdd = Array.IndexOf(_proposedMaps, null);

        if (indexToAdd == -1)
        {
            PrintToChat(player, "Maximum number of nominated maps");
            return;
        }

        foreach (var map in _proposedMaps)
        {
            if (map != option.Text) continue;
            PrintToChat(player, "The map you chose has already been nominated.");
            return;
        }

        foreach (var playedMap in _playedMaps)
        {
            if (playedMap != option.Text) continue;
            PrintToChat(player, "The map you chose was recently played and cannot be nominated");
            return;
        }

        var user = _usersArray[player.EntityIndex!.Value.Value];
        if (!string.IsNullOrEmpty(user!.ProposedMaps))
        {
            var buffer = user.ProposedMaps;

            for (int i = 0; i < _proposedMaps.Length; i++)
            {
                if (_proposedMaps[i] == buffer)
                    _proposedMaps[i] = option.Text;
            }
        }
        else
            _proposedMaps[indexToAdd] = option.Text;

        user.ProposedMaps = option.Text;
        PrintToChatAll($" Player '{player.PlayerName}' nominated a map '{option.Text}'");
    }

    private void VoteMap(bool forced)
    {
        _isVotingActive = false;
        var nominateMenu = new ChatMenu("RTV");
        var mapList = File.ReadAllLines(Path.Combine(ModuleDirectory, "maps.txt"));
        var newMapList = mapList.Except(_playedMaps).Except(_proposedMaps)
            .Select(map => map.StartsWith("ws:") ? map.Substring(3) : map)
            .ToList();

        if (mapList.Length < 7)
        {
            for (int i = 0; i < mapList.Length-1; i++)
            {
                if (_proposedMaps[i] == null)
                {
                    if (newMapList.Count > 0)
                    {
                        var rand = new Random().Next(newMapList.Count);
                        _proposedMaps[i] = newMapList[rand];
                        newMapList.RemoveAt(rand);
                    }
                    else
                    {
                        var unplayedMaps = _playedMaps.Except(_proposedMaps).Where(map => map != NativeAPI.GetMapName())
                            .ToList();
                        if (unplayedMaps.Count > 0)
                        {
                            var rand = new Random().Next(unplayedMaps.Count);
                            _proposedMaps[i] = unplayedMaps[rand];
                            unplayedMaps.RemoveAt(rand);
                        }
                    }
                }
                nominateMenu.AddMenuOption($"{_proposedMaps[i]}", (controller, option) =>
                {
                    if (!optionCounts.TryGetValue(option.Text, out int count))
                        optionCounts[option.Text] = 1;
                    else
                        optionCounts[option.Text] = count + 1;
                    _votedMap++;
                    PrintToChatAll($"{controller.PlayerName} has selected {option.Text}");
                });
            }
        }
        else
        {
            for (int i = 0; i < 7; i++)
            {
                if (_proposedMaps[i] == null)
                {
                    if (newMapList.Count > 0)
                    {
                        var rand = new Random().Next(newMapList.Count);
                        _proposedMaps[i] = newMapList[rand];
                        newMapList.RemoveAt(rand);
                    }
                    else
                    {
                        var unplayedMaps = _playedMaps.Except(_proposedMaps).Where(map => map != NativeAPI.GetMapName())
                            .ToList();
                        if (unplayedMaps.Count > 0)
                        {
                            var rand = new Random().Next(unplayedMaps.Count);
                            _proposedMaps[i] = unplayedMaps[rand];
                            unplayedMaps.RemoveAt(rand);
                        }
                    }
                }

                nominateMenu.AddMenuOption($"{_proposedMaps[i]}", (controller, option) =>
                {
                    if (!optionCounts.TryGetValue(option.Text, out int count))
                        optionCounts[option.Text] = 1;
                    else
                        optionCounts[option.Text] = count + 1;
                    _votedMap++;
                    PrintToChatAll($"{controller.PlayerName} has selected {option.Text}");
                });
            }
        }

        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
        foreach (var player in playerEntities)
        {
            ChatMenus.OpenMenu(player, nominateMenu);
        }

        AddTimer(20.0f, () => TimerVoteMap(forced));
    }

    private void TimerVoteMap(bool forced)
    {
        if (optionCounts.Count == 0 && forced)
        {
            PrintToChatAll("No votes received for Rock the Vote, keeping current map.");
            ResetData();
            return;
        }

        if (_votedMap == 0 && !forced)
        {
            var random = Random.Shared;
            _selectedMap = _proposedMaps[random.Next(_proposedMaps.Length)];
            PrintToChatAll($"During the voting process, the {_selectedMap} map was selected");
        }

        if (_selectedMap != null && !forced)
        {
            if (IsTimeLimit)
            {
                AddTimer(_config.VotingTimeInterval * 60.0f, () =>
                {
                    Server.ExecuteCommand(IsWsMaps(_selectedMap)
                        ? $"ds_workshop_changelevel {_selectedMap}"
                        : $"map {_selectedMap}");
                });
                return;
            }

            return;
        }

        _selectedMap = optionCounts.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;

        if (forced)
        {
            PrintToChatAll($"During the voting process, the {_selectedMap} map was selected");
            Task.Delay(TimeSpan.FromSeconds(5)).ContinueWith(_ =>
            {
                Server.ExecuteCommand(IsWsMaps(_selectedMap)
                    ? $"ds_workshop_changelevel {_selectedMap}"
                    : $"map {_selectedMap}");
            });
            return;
        }

        PrintToChatAll($"During the voting process, the {_selectedMap} map was selected");
        if (!IsTimeLimit) return;
        AddTimer(_config.VotingTimeInterval * 60.0f, () =>
        {
            Server.ExecuteCommand(IsWsMaps(_selectedMap)
                ? $"ds_workshop_changelevel {_selectedMap}"
                : $"map {_selectedMap}");
        });
    }

    private bool IsWsMaps(string selectMap)
    {
        var mapsPath = Path.Combine(ModuleDirectory, "maps.txt");
        var mapList = File.ReadAllLines(mapsPath);

        return mapList.Any(map => map.Trim() == "ws:" + selectMap);
        ;
    }

    private void PrintToChat(CCSPlayerController controller, string msg)
    {
        controller.PrintToChat($"\x08[ \x0CRockTheVote \x08] {msg}");
    }

    private void PrintToChatAll(string msg)
    {
        Server.PrintToChatAll($"\x08[ \x0CRockTheVote \x08] {msg}");
    }

    private Config LoadConfig()
    {
        var configPath = Path.Combine(ModuleDirectory, "settings.json");

        if (!File.Exists(configPath)) return CreateConfig(configPath);

        var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath))!;

        return config;
    }

    private Config CreateConfig(string configPath)
    {
        var config = new Config
        {
            Needed = 0.6,
            VotingRoundInterval = 5,
            VotingTimeInterval = 10,
            RoundsBeforeNomination = 6,
        };

        File.WriteAllText(configPath,
            JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine("[MapChooser] The configuration was successfully saved to a file: " + configPath);
        Console.ResetColor();

        return config;
    }

    private void ResetData()
    {
        _isVotingActive = true;
        _votedMap = 0;
        optionCounts = new Dictionary<string, int>(0);
        _votedRtv = 0;
        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
        foreach (var player in playerEntities)
        {
            _usersArray[player.EntityIndex!.Value.Value].VotedRtv = false;
            _usersArray[player.EntityIndex!.Value.Value].ProposedMaps = null;
        }

        for (var i = 0; i < _proposedMaps.Length; i++)
        {
            _proposedMaps[i] = null;
        }
    }
}

public class Config
{
    public int RoundsBeforeNomination { get; set; }
    public float VotingTimeInterval { get; set; }
    public int VotingRoundInterval { get; set; }
    public double Needed { get; set; }
}

public class Users
{
    public required string ProposedMaps { get; set; }
    public bool VotedRtv { get; set; }
}