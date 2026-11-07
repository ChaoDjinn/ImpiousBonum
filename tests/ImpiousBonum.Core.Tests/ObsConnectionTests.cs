using System.Text.Json.Nodes;
using ImpiousBonum.Core.Obs;

namespace ImpiousBonum.Core.Tests;

public sealed class ObsConnectionTests
{
    [Fact]
    public void The_password_check_matches_obs_websocket_s_own_example() =>
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsConnection.Authentication("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));

    [Theory]
    [InlineData(null, "ws://localhost:4455/")]
    [InlineData("", "ws://localhost:4455/")]
    [InlineData("192.168.1.20", "ws://192.168.1.20:4455/")]
    [InlineData("streampc:4460", "ws://streampc:4460/")]
    [InlineData("ws://streampc", "ws://streampc:4455/")]
    [InlineData("ws://streampc:80", "ws://streampc/")]
    [InlineData("[::1]", "ws://[::1]:4455/")]
    [InlineData("[::1]:4460", "ws://[::1]:4460/")]
    [InlineData("http://streampc", null)]
    [InlineData("not an address", null)]
    public void Addresses_are_read_the_way_people_type_them(string? typed, string? expected) =>
        Assert.Equal(expected, ObsRequest.AddressOf(typed)?.ToString());

    [Fact]
    public async Task Requests_go_to_OBS_in_one_batch_and_its_answers_come_back_in_order()
    {
        await using var obs = new FakeObs("secret");
        obs.Answer = (type, _) => type == "GetCurrentProgramScene"
            ? (true, new JsonObject { ["currentProgramSceneName"] = "Gaming" })
            : (false, null);
        await using var connection = new ObsConnection(() => obs.Request("secret"), () => true);

        var results = await connection.SendAsync([new ObsCall("GetCurrentProgramScene"), new ObsCall("GetReplayBufferStatus")]);

        Assert.NotNull(results);
        Assert.Equal(ObsState.Connected, connection.State);
        Assert.True(results[0].Ok);
        Assert.Equal("Gaming", (string?)results[0].Data?["currentProgramSceneName"]);
        Assert.False(results[1].Ok);
        Assert.Equal(["GetCurrentProgramScene", "GetReplayBufferStatus"], obs.Requests.Select(r => r.Type));
    }

    [Fact]
    public async Task One_request_carries_its_data_and_says_whether_OBS_did_it()
    {
        await using var obs = new FakeObs();
        await using var connection = new ObsConnection(() => obs.Request(null), () => true);

        Assert.True(await connection.SendAsync("SetCurrentProgramScene", new JsonObject { ["sceneName"] = "BRB" }));
        Assert.Equal("BRB", (string?)obs.Requests.Single().Data?["sceneName"]);
    }

    [Fact]
    public async Task A_wrong_or_missing_password_is_told_apart_from_OBS_being_unreachable()
    {
        await using var obs = new FakeObs("secret");
        await using (var wrong = new ObsConnection(() => obs.Request("guess"), () => true))
        {
            Assert.Null(await wrong.SendAsync([new ObsCall("GetStats")]));
            Assert.Equal(ObsState.WrongPassword, wrong.State);
        }
        await using (var none = new ObsConnection(() => obs.Request(null), () => true))
        {
            Assert.Null(await none.SendAsync([new ObsCall("GetStats")]));
            Assert.Equal(ObsState.WrongPassword, none.State);
        }

        var nowhere = new ObsRequest(new Uri("ws://localhost:1/"), null);
        await using var unreachable = new ObsConnection(() => nowhere, () => true);
        Assert.Null(await unreachable.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(ObsState.Unreachable, unreachable.State);
        Assert.Empty(obs.Requests);
    }

    [Fact]
    public async Task Nothing_is_tried_while_it_s_off_or_OBS_isn_t_running_here()
    {
        await using var obs = new FakeObs();
        ObsRequest? settings = null;
        var running = false;
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        await using var connection = new ObsConnection(() => settings, () => running, time);

        Assert.Null(await connection.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(ObsState.Off, connection.State);

        settings = obs.Request(null);
        Assert.Null(await connection.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(ObsState.NotRunning, connection.State);

        // OBS starts: it's looked for again a few seconds later, not on every request.
        running = true;
        Assert.Null(await connection.SendAsync([new ObsCall("GetStats")]));
        time.Now += ObsConnection.RetryAfter;
        Assert.NotNull(await connection.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(1, obs.Connections);
    }

    [Fact]
    public async Task After_OBS_closes_the_connection_it_connects_again()
    {
        await using var obs = new FakeObs();
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        await using var connection = new ObsConnection(() => obs.Request(null), () => true, time);
        Assert.NotNull(await connection.SendAsync([new ObsCall("GetStats")]));

        await obs.DropAsync();
        await WaitFor(() => connection.State != ObsState.Connected);
        time.Now += ObsConnection.RetryAfter;

        Assert.NotNull(await connection.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(2, obs.Connections);
    }

    [Fact]
    public async Task A_new_address_or_password_connects_again_with_it()
    {
        await using var obs = new FakeObs("secret");
        var password = "guess";
        var time = new FakeTime(DateTimeOffset.UnixEpoch);
        await using var connection = new ObsConnection(() => obs.Request(password), () => true, time);
        Assert.Null(await connection.SendAsync([new ObsCall("GetStats")]));

        password = "secret";
        time.Now += ObsConnection.RetryAfter;
        Assert.NotNull(await connection.SendAsync([new ObsCall("GetStats")]));
        Assert.Equal(ObsState.Connected, connection.State);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
            await Task.Delay(50);
        Assert.True(condition());
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
