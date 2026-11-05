using System.Net;
using System.Text.Json;
using ImpiousBonum.Core.Games;
using ImpiousBonum.Core.Metrics;
using ImpiousBonum.Core.Providers;
using ImpiousBonum.Core.Steam;

namespace ImpiousBonum.Core.Tests;

public sealed class SteamFriendsTests
{
    private const string Me = "76561197960287930";
    private const string Sam = "76561197960287931";
    private const string Priya = "76561197960287932";
    private const string Alex = "76561197960287933";
    private const string Kai = "76561197960287934";

    private const string RefusedKey =
        "<html><head><title>Forbidden</title></head><body><h1>Forbidden</h1>Access is denied. Retrying will not help. Please verify your <pre>key=</pre> parameter.</body></html>";

    private static readonly DateTimeOffset Now = new(2026, 11, 5, 22, 0, 0, TimeSpan.Zero);

    private static readonly string Resolved = $$"""{ "response": { "steamid": "{{Me}}", "success": 1 } }""";
    private static readonly string FriendList = $$"""
        { "friendslist": { "friends": [
            { "steamid": "{{Sam}}", "relationship": "friend", "friend_since": 1500000000 },
            { "steamid": "{{Priya}}", "relationship": "friend", "friend_since": 1500000001 },
            { "steamid": "{{Alex}}", "relationship": "friend", "friend_since": 1500000002 },
            { "steamid": "{{Kai}}", "relationship": "friend", "friend_since": 1500000003 }
        ] } }
        """;
    private static readonly string Summaries = $$"""
        { "response": { "players": [
            { "steamid": "{{Priya}}", "personaname": "Priya", "personastate": 1 },
            { "steamid": "{{Sam}}", "personaname": "Sam", "personastate": 1, "gameextrainfo": "Hades", "gameid": "1145360" },
            { "steamid": "{{Alex}}", "personaname": "Alex", "personastate": 3 },
            { "steamid": "{{Kai}}", "personaname": "Kai", "personastate": 0 }
        ] } }
        """;
    private const string Achievements = """
        { "playerstats": { "steamID": "76561197960287930", "gameName": "Hades", "success": true, "achievements": [
            { "apiname": "A", "achieved": 1, "unlocktime": 1600000000 },
            { "apiname": "B", "achieved": 0, "unlocktime": 0 },
            { "apiname": "C", "achieved": 1, "unlocktime": 1600000001 },
            { "apiname": "D", "achieved": 0, "unlocktime": 0 }
        ] } }
        """;

    [Theory]
    [InlineData(Me, Me)]
    [InlineData(" https://steamcommunity.com/profiles/76561197960287930/ ", Me)]
    [InlineData("steamcommunity.com/id/gabelogannewell", "gabelogannewell")]
    [InlineData("https://steamcommunity.com/id/Some_Name-1/?l=english", "Some_Name-1")]
    [InlineData("gabelogannewell", "gabelogannewell")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData("not a profile!", null)]
    [InlineData("https://steamcommunity.com/id/", null)]
    public void A_profile_is_its_id_or_its_name_however_it_was_typed(string? typed, string? expected) =>
        Assert.Equal(expected, SteamWeb.Profile(typed));

    [Fact]
    public void An_account_needs_a_key_and_a_profile_and_never_shows_its_key()
    {
        var account = SteamWeb.Account(" SECRETKEY ", "steamcommunity.com/id/me")!;
        Assert.Equal(new SteamAccount("SECRETKEY", "me"), account);
        Assert.False(account.IsId);
        Assert.True(SteamWeb.Account("k", Me)!.IsId);
        Assert.DoesNotContain("SECRETKEY", account.ToString());
        Assert.Null(SteamWeb.Account(null, Me));
        Assert.Null(SteamWeb.Account(" ", Me));
        Assert.Null(SteamWeb.Account("k", ""));
        Assert.False(SteamWeb.IsSteamId("1234"));
    }

    [Fact]
    public void Requests_go_to_steam_s_web_api_with_the_key()
    {
        Assert.Equal("https://api.steampowered.com/ISteamUser/ResolveVanityURL/v1/?key=a%2Bb&vanityurl=me", SteamWeb.ResolveUrl(new SteamAccount("a+b", "me")));
        Assert.Equal($"https://api.steampowered.com/ISteamUser/GetFriendList/v1/?key=k&steamid={Me}&relationship=friend", SteamWeb.FriendsUrl("k", Me));
        Assert.Equal($"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key=k&steamids={Sam},{Priya}", SteamWeb.SummariesUrl("k", [Sam, Priya]));
        Assert.Equal($"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v1/?key=k&steamid={Me}&appid=1145360", SteamWeb.AchievementsUrl("k", Me, 1145360));
    }

    [Fact]
    public void Steam_s_replies_are_read_for_ids_friends_and_achievements()
    {
        Assert.Equal(Me, SteamWeb.ResolvedId(Resolved, "me"));
        var noSuch = Assert.Throws<SteamWebException>(() => SteamWeb.ResolvedId("""{ "response": { "success": 42, "message": "No match" } }""", "nobody"));
        Assert.Contains("nobody", noSuch.Message);

        Assert.Equal([Sam, Priya, Alex, Kai], SteamWeb.FriendIds(FriendList));
        Assert.Empty(SteamWeb.FriendIds("{}"));

        // Offline friends aren't listed.
        Assert.Equal(
            [
                new SteamFriend("Priya", SteamPresence.Online, null),
                new SteamFriend("Sam", SteamPresence.Online, "Hades"),
                new SteamFriend("Alex", SteamPresence.Away, null),
            ], SteamWeb.OnlineFriends(Summaries));

        Assert.Equal(new AchievementCount(2, 4), SteamWeb.Achievements(HttpStatusCode.OK, Achievements));
        Assert.Equal(50, new AchievementCount(2, 4).Percent);
        // A game without achievements.
        Assert.Null(SteamWeb.Achievements(HttpStatusCode.BadRequest, """{ "playerstats": { "error": "Requested app has no stats", "success": false } }"""));
        Assert.Null(SteamWeb.Achievements(HttpStatusCode.BadRequest, "<html>Bad Request</html>"));
        Assert.Throws<SteamWebException>(() => SteamWeb.Achievements(HttpStatusCode.Forbidden, """{ "playerstats": { "error": "Profile is not public", "success": false } }"""));
        Assert.Throws<JsonException>(() => SteamWeb.Achievements(HttpStatusCode.OK, "<html>?</html>"));

        Assert.Throws<SteamWebException>(() => SteamWeb.ThrowIfKeyRefused(HttpStatusCode.Forbidden, RefusedKey));
        SteamWeb.ThrowIfKeyRefused(HttpStatusCode.Unauthorized, "<html>Unauthorized</html>");
        SteamWeb.ThrowIfKeyRefused(HttpStatusCode.OK, Resolved);
    }

    [Fact]
    public async Task Friends_online_come_every_minute_the_list_every_ten_and_achievements_while_a_steam_game_is_in_front()
    {
        var steam = new FakeSteam();
        var time = new FakeTime(Now);
        SteamAccount? account = null;
        using var provider = new SteamFriendsProvider(() => account, steam.Get, time);
        var store = new MetricStore();
        provider.Initialize(store);

        // Off: nothing is asked.
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Empty(steam.Asked);
        Assert.Null(Text(store, SteamFriendsProvider.Status));

        account = new SteamAccount("k", "me");
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["ResolveVanityURL", "GetFriendList", "GetPlayerSummaries"], steam.Asked);
        var friends = Assert.IsAssignableFrom<IReadOnlyList<SteamFriend>>(store.GetAttachment(SteamFriendsProvider.Friends));
        Assert.Equal(["Sam", "Priya", "Alex"], friends.Select(f => f.Name));
        Assert.Equal(3, Number(store, SteamFriendsProvider.Online));
        Assert.Equal(1, Number(store, SteamFriendsProvider.Playing));
        Assert.Equal("3 friends online, 1 playing.", Text(store, SteamFriendsProvider.Status));
        Assert.Null(Text(store, SteamFriendsProvider.Achievements));

        // Within the minute: nothing more. After it: who's online again. After ten: the list too.
        steam.Asked.Clear();
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Empty(steam.Asked);
        time.Advance(SteamFriendsProvider.FriendsEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["GetPlayerSummaries"], steam.Asked);
        steam.Asked.Clear();
        time.Advance(SteamFriendsProvider.FriendListEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["GetFriendList", "GetPlayerSummaries"], steam.Asked);

        // A Steam game comes to the front: its achievements, straight away and then every two minutes.
        steam.Asked.Clear();
        store.SetAttachment(GameArtProvider.Current, new Game("Hades", 1145360, new GameArt(null, null, null, null), GameStore.Steam, "1145360"));
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal(["GetPlayerAchievements"], steam.Asked);
        Assert.Equal("2 / 4", Text(store, SteamFriendsProvider.Achievements));
        Assert.Equal(50, Number(store, SteamFriendsProvider.AchievementsPercent));

        // Gone from the front: so are its achievements.
        store.SetAttachment(GameArtProvider.Current, null);
        time.Advance(provider.Interval);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(Text(store, SteamFriendsProvider.Achievements));

        // Turned off: every reading goes.
        account = null;
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Null(store.GetAttachment(SteamFriendsProvider.Friends));
        Assert.Null(Number(store, SteamFriendsProvider.Online));
        Assert.Null(Text(store, SteamFriendsProvider.Status));
    }

    [Fact]
    public async Task A_refused_key_says_so_and_waits_before_asking_again()
    {
        var steam = new FakeSteam { Refuse = true };
        var time = new FakeTime(Now);
        using var provider = new SteamFriendsProvider(() => new SteamAccount("bad", Me), steam.Get, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.StartsWith("Steam didn't accept the key.", Text(store, SteamFriendsProvider.Status));
        Assert.Null(Number(store, SteamFriendsProvider.Online));
        Assert.Equal(["GetFriendList"], steam.Asked);

        time.Advance(SteamFriendsProvider.FriendsEvery);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Single(steam.Asked);

        // A good key later on.
        steam.Refuse = false;
        time.Advance(SteamFriendsProvider.RetryAfter);
        await provider.SampleAsync(store, CancellationToken.None);
        Assert.Equal("3 friends online, 1 playing.", Text(store, SteamFriendsProvider.Status));
    }

    [Fact]
    public async Task A_private_friends_list_or_steam_out_of_reach_says_so()
    {
        var steam = new FakeSteam { PrivateFriends = true };
        var time = new FakeTime(Now);
        using var provider = new SteamFriendsProvider(() => new SteamAccount("k", Me), steam.Get, time);
        var store = new MetricStore();
        provider.Initialize(store);

        await provider.SampleAsync(store, CancellationToken.None);
        Assert.StartsWith("Your friends list is private", Text(store, SteamFriendsProvider.Status));
        Assert.Equal(0, Number(store, SteamFriendsProvider.Online));

        steam.Offline = true;
        using var other = new SteamFriendsProvider(() => new SteamAccount("k", Me), steam.Get, time);
        await other.SampleAsync(store, CancellationToken.None);
        Assert.StartsWith("Couldn't reach Steam.", Text(store, SteamFriendsProvider.Status));
    }

    [Fact]
    public void How_many_friends_are_online_reads_simply()
    {
        Assert.Equal("No friends online.", SteamFriendsProvider.FriendsOnline([]));
        Assert.Equal("1 friend online.", SteamFriendsProvider.FriendsOnline([new SteamFriend("Sam", SteamPresence.Online, null)]));
        Assert.Equal("2 friends online, 1 playing.", SteamFriendsProvider.FriendsOnline(
            [new SteamFriend("Sam", SteamPresence.Online, "Hades"), new SteamFriend("Alex", SteamPresence.Away, null)]));
    }

    private static double? Number(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Value : null;

    private static string? Text(MetricStore store, string id) => store.TryGet(id, out var sample) ? sample.Text : null;

    /// <summary>Steam's Web API, answering from the replies above and noting which methods were asked for.</summary>
    private sealed class FakeSteam
    {
        public List<string> Asked { get; } = [];

        public bool Refuse { get; set; }

        public bool PrivateFriends { get; set; }

        public bool Offline { get; set; }

        public Task<(HttpStatusCode Status, string Body)> Get(string url, CancellationToken cancellationToken)
        {
            if (Offline)
                throw new HttpRequestException("No such host is known.");
            var method = url.Split('/')[4];
            Asked.Add(method);
            if (Refuse)
                return Task.FromResult((HttpStatusCode.Forbidden, RefusedKey));
            return Task.FromResult(method switch
            {
                "ResolveVanityURL" => (HttpStatusCode.OK, Resolved),
                "GetFriendList" when PrivateFriends => (HttpStatusCode.Unauthorized, "<html><head><title>Unauthorized</title></head></html>"),
                "GetFriendList" => (HttpStatusCode.OK, FriendList),
                "GetPlayerSummaries" => (HttpStatusCode.OK, Summaries),
                "GetPlayerAchievements" => (HttpStatusCode.OK, Achievements),
                _ => (HttpStatusCode.NotFound, ""),
            });
        }
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
