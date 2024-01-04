using System.Diagnostics;
using System.Reflection;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Flurl.Http;
using Newtonsoft.Json;

namespace StatusSwitcher;

// StatusSwitcher - register switches in the PluralKit API automatically
// Written by Madoka (Nightshade System) 4/1/2024

public static class Program
{
    private static DiscordClient Discord = null!;
    private static Config CurrentConfig = null!;
    private static Version CurrentVersion = Assembly.GetExecutingAssembly().GetName().Version!;
    
    private static Dictionary<string, Member> LoadedMembers = new();

    private struct Member
    {
        public string Name;
    }

    private class Config
    {
        // What do you think?
        public string Token = null!;
        // The user ID to pay attention to
        public ulong UserID = 0;
        // PluralKit API token
        public string PluralKitToken = null!;
        // Channel to log switches to
        public ulong LogChannel = 0;
        // The map of emotes to PK member IDs
        public Dictionary<string, string> MemberMap = null!;
    }
    
    public static async Task Main(string[] args)
    {
        var configPath = Environment.GetEnvironmentVariable("CONFIG_PATH")
                         ?? throw new Exception("CONFIG_PATH unset");

        var configFile = await File.ReadAllTextAsync(configPath);
        // System.Text.Json here does not deserialise the config properly.
        // Beats me, but Newtonsoft.Json works fine. ~Madoka
        var config = JsonConvert.DeserializeObject<Config>(configFile)!;
        CurrentConfig = config;
        
        // Let's populate the member map now
        await PopulateMemberMap();

        Discord = new(new DiscordConfiguration
        {
            Token = config.Token,
            TokenType = TokenType.Bot,
            Intents = DiscordIntents.All
        });

        Discord.PresenceUpdated += OnPresenceUpdate;

        await Discord.UpdateStatusAsync(new DiscordActivity()
        {
            ActivityType = ActivityType.Playing,
            Name = $"StatusSwitcher v{CurrentVersion}"
        });

        await Discord.ConnectAsync();
        await Task.Delay(-1);
    }

    private static async Task PopulateMemberMap()
    {
        var stopwatch = Stopwatch.GetTimestamp();
        Console.WriteLine("Loading members from PK api, this might take a while");
        
        var reqString = await "https://api.pluralkit.me/v2/systems/@me/members"
            .WithHeader("User-Agent",  $"StatusSwitcher/{CurrentVersion}")
            .WithHeader("Accept", "application/json")
            .WithHeader("Authorization", CurrentConfig.PluralKitToken)
            .GetStringAsync();
        
        // Again, System.Text.Json doesn't seem to do so well with Unicode codepoints.
        // Also, I can't seem to deserialise into a Dictionary, so dynamic it is.
        var req = JsonConvert.DeserializeObject<List<dynamic>>(reqString);
        
        var timeAfter = Stopwatch.GetElapsedTime(stopwatch);
        Console.WriteLine($"PK API took {timeAfter.TotalSeconds} seconds to respond");
        
        var reversedDict = CurrentConfig.MemberMap
            .ToDictionary(x => x.Value, x => x.Key);
        
        foreach (var key in req!)
        {
            var id = (string)key["id"];
            if (!reversedDict.ContainsKey(id)) continue;
            LoadedMembers[id] = new Member
            {
                Name = (string)key["name"]
            };
        }
    }

    private static async Task OnPresenceUpdate(DiscordClient _, PresenceUpdateEventArgs args)
    {
        if (args.User.Id != CurrentConfig.UserID) return;
        
        var emote = args.PresenceAfter.Activity.CustomStatus.Emoji.Name;
        
        // TODO: register switch-out (configurable) if emote is missing or invalid
        if (!CurrentConfig.MemberMap.ContainsKey(emote)) return;
        
        var memberId = CurrentConfig.MemberMap[emote];
        var memberName = LoadedMembers[memberId].Name;
        Console.WriteLine($"Member id for emote is {memberId}, corresponding to {memberName}");

        try
        {
            await "https://api.pluralkit.me/v2/systems/@me/switches"
                .WithHeader("User-Agent", $"StatusSwitcher/{CurrentVersion}")
                .WithHeader("Accept", "application/json")
                .WithHeader("Authorization", CurrentConfig.PluralKitToken)
                .PostJsonAsync(new
                {
                    members = new List<string> { memberId }
                });
        }
        catch (FlurlHttpException e)
        {
            if (e.StatusCode != 400) throw;
            
            var res = await e.GetResponseJsonAsync<Dictionary<string, dynamic>>();
            var code = (int)res["code"];
            
            // 40004 = 'Member list identical to current fronter list.'
            if (code != 40004) throw;
            
            Console.WriteLine("w: tried to register identical switch, ignoring");
            return;
        }

        // Switch successful, log a message
        var logChannel = await Discord.GetChannelAsync(CurrentConfig.LogChannel);
        await logChannel.SendMessageAsync(
            $"<@{CurrentConfig.UserID}> Switch registered. Current fronter is now {memberName}.");
    }
}