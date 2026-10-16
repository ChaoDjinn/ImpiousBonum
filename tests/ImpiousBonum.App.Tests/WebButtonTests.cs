using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Controls;
using ImpiousBonum.App.Layout;
using ImpiousBonum.App.Shell;
using ImpiousBonum.App.Widgets;
using ImpiousBonum.Core.Metrics;

namespace ImpiousBonum.App.Tests;

public sealed class WebButtonTests
{
    private const string Token = "Bearer secret-token-123";
    private const string ScriptAddress = "http://homeassistant.local:8123/api/services/script/turn_on";

    private static WidgetAction Web(string target = ScriptAddress, string method = "POST", string body = "", string contentType = "", string header = "") =>
        new(WidgetAction.Web, target) { Method = method, Body = body, ContentType = contentType, Header = header };

    private static JsonObject Buttons(params JsonObject[] buttons) => new()
    {
        ["type"] = "buttons", ["x"] = 0, ["y"] = 0, ["width"] = 400, ["height"] = 200,
        ["buttons"] = new JsonArray(buttons),
    };

    private static JsonObject MovieNight() => new()
    {
        ["label"] = "Movie night", ["icon"] = "moon", ["action"] = "web", ["target"] = ScriptAddress,
        ["body"] = """{"entity_id": "script.movie_night"}""", ["header"] = "Authorization: " + Token,
    };

    [Fact]
    public async Task A_web_button_sends_its_method_body_and_header()
    {
        using var json = ActionRunner.WebRequest(Web(body: """{"entity_id": "script.movie_night"}""", header: "Authorization: " + Token), out var problem)!;
        Assert.Null(problem);
        Assert.Equal(HttpMethod.Post, json.Method);
        Assert.Equal(ScriptAddress, json.RequestUri!.ToString());
        Assert.Equal(Token, Assert.Single(json.Headers.GetValues("Authorization")));
        Assert.Equal("application/json; charset=utf-8", json.Content!.Headers.ContentType!.ToString());
        Assert.Equal("""{"entity_id": "script.movie_night"}""", await json.Content.ReadAsStringAsync());

        // ntfy shows a plain-text body as the notification.
        using var text = ActionRunner.WebRequest(Web("https://ntfy.sh/my-desk", body: "Dinner's ready"), out _)!;
        Assert.Equal("text/plain; charset=utf-8", text.Content!.Headers.ContentType!.ToString());

        using var put = ActionRunner.WebRequest(Web(method: "PUT", body: "on", contentType: "application/x-www-form-urlencoded"), out _)!;
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("application/x-www-form-urlencoded", put.Content!.Headers.ContentType!.MediaType);

        // A GET sends no body, and an empty method is a POST.
        using var get = ActionRunner.WebRequest(Web(method: "GET", body: "ignored"), out _)!;
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Null(get.Content);
        using var plain = ActionRunner.WebRequest(Web(method: ""), out _)!;
        Assert.Equal(HttpMethod.Post, plain.Method);
        Assert.Null(plain.Content);
    }

    [Theory]
    [InlineData("", "POST", "", "")]
    [InlineData("homeassistant.local/api/webhook/lights", "POST", "", "")]
    [InlineData("ftp://example.com/file", "POST", "", "")]
    [InlineData(ScriptAddress, "DELETE", "", "")]
    [InlineData(ScriptAddress, "POST", "json please", "")]
    [InlineData(ScriptAddress, "POST", "", "Bearer secret-token-123")]
    [InlineData(ScriptAddress, "POST", "", "Content-Type: Bearer secret-token-123")]
    [InlineData(ScriptAddress, "POST", "", "Bad Name: Bearer secret-token-123")]
    [InlineData(ScriptAddress, "POST", "", "Authorization: Bearer secret-token-123\r\nX-Evil: 1")]
    public void Web_buttons_that_cannot_send_say_why_without_the_header(string target, string method, string contentType, string header)
    {
        Assert.Null(ActionRunner.WebRequest(Web(target, method, "{}", contentType, header), out var problem));
        Assert.False(string.IsNullOrEmpty(problem));
        Assert.DoesNotContain("secret", problem);

        var done = new List<bool>();
        Assert.Equal(problem, new ActionRunner(_ => null).Run(Web(target, method, "{}", contentType, header) with { Done = done.Add }));
        Assert.Equal([false], done);
    }

    [Fact]
    public async Task A_request_goes_off_the_ui_thread_and_tells_the_button_how_it_went()
    {
        var onPool = new List<bool>();
        var answer = HttpStatusCode.OK;
        var logged = new List<string>();
        var runner = new ActionRunner(_ => null)
        {
            WebSender = (_, _) =>
            {
                onPool.Add(Thread.CurrentThread.IsThreadPoolThread);
                return answer == 0
                    ? throw new HttpRequestException("No such host is known.")
                    : Task.FromResult(new HttpResponseMessage(answer) { ReasonPhrase = answer.ToString() });
            },
            Failed = logged.Add,
        };

        async Task<bool> Press()
        {
            var done = new TaskCompletionSource<bool>();
            // Pressed on a thread of its own, standing in for the UI thread: the request mustn't run on it.
            string? problem = "not pressed";
            var ui = new Thread(() => problem = runner.Run(Web(header: "Authorization: " + Token) with { Done = done.SetResult }));
            ui.Start();
            ui.Join();
            Assert.Null(problem);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.True(await Press());
        Assert.Empty(logged);
        Assert.True(Assert.Single(onPool));

        answer = HttpStatusCode.Unauthorized;
        Assert.False(await Press());
        Assert.Equal("Button (web): homeassistant.local answered 401 Unauthorized", Assert.Single(logged));

        answer = 0;
        Assert.False(await Press());
        Assert.Equal("Button (web): couldn't reach homeassistant.local: No such host is known.", logged[^1]);
        Assert.All(logged, line => Assert.DoesNotContain("secret", line));
    }

    [Fact]
    public async Task A_real_request_reaches_the_server_with_everything_the_button_sends()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var logged = new List<string>();
        var runner = new ActionRunner(_ => null) { Failed = logged.Add };
        var action = Web($"http://127.0.0.1:{port}/api/services/script/turn_on", body: """{"entity_id": "script.movie_night"}""", header: "Authorization: " + Token);

        async Task<(bool Worked, string Request)> Press(string status)
        {
            var done = new TaskCompletionSource<bool>();
            Assert.Null(runner.Run(action with { Done = done.SetResult }));
            var request = await Serve(listener, status);
            return (await done.Task.WaitAsync(TimeSpan.FromSeconds(10)), request);
        }

        var (worked, request) = await Press("204 No Content");
        Assert.True(worked);
        Assert.StartsWith("POST /api/services/script/turn_on HTTP/1.1", request);
        Assert.Contains("Authorization: " + Token, request);
        Assert.Contains("Content-Type: application/json; charset=utf-8", request);
        Assert.EndsWith("""{"entity_id": "script.movie_night"}""", request);

        (worked, _) = await Press("401 Unauthorized");
        Assert.False(worked);
        Assert.Equal("Button (web): 127.0.0.1 answered 401 Unauthorized", Assert.Single(logged));
    }

    /// <summary>Answers one request with <paramref name="status"/> and returns it, headers and body.</summary>
    private static async Task<string> Serve(TcpListener listener, string status)
    {
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));
        using var stream = client.GetStream();
        var received = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            received.Write(buffer, 0, read);
            var text = Encoding.UTF8.GetString(received.ToArray());
            var end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (read == 0 || (end >= 0 && Encoding.UTF8.GetByteCount(text[(end + 4)..]) >= ContentLength(text[..end])))
                break;
        }
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        return Encoding.UTF8.GetString(received.ToArray());
    }

    private static int ContentLength(string head) =>
        head.Split("\r\n").Select(line => line.Split(':', 2)).Where(p => p.Length == 2 && p[0].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            .Select(p => int.Parse(p[1].Trim())).FirstOrDefault();

    [Fact]
    public void A_web_button_waits_for_its_answer_then_lights_up()
    {
        Sta.Run(() =>
        {
            var theme = Theme.From(new ThemeSettings());
            var widget = WidgetFactory.Create(Buttons(MovieNight()), theme);
            var host = new Grid();
            host.Children.Add(widget);
            var requested = new List<WidgetAction>();
            host.AddHandler(Widget.ActionEvent, new EventHandler<WidgetActionEventArgs>((_, e) =>
            {
                e.Handled = true;
                requested.Add(e.Action);
            }));
            var target = Assert.Single(((Panel)widget.Children[0]).Children.OfType<TapTarget>());
            Assert.Equal("Sends a web request to homeassistant.local", target.ToolTip);

            target.Tap();
            var action = Assert.Single(requested);
            Assert.Equal((WidgetAction.Web, ScriptAddress, "POST", "Authorization: " + Token), (action.Kind, action.Target, action.Method, action.Header));
            Assert.Equal("""{"entity_id": "script.movie_night"}""", action.Body);
            Assert.True(target.Waiting);

            // Presses while it waits don't stack up.
            target.Tap();
            Assert.Single(requested);

            action.Done!(true);
            Assert.False(target.Waiting);
            Assert.Same(theme.Accent, target.Light);

            target.Tap();
            Assert.Equal(2, requested.Count);
            requested[1].Done!(false);
            Assert.Same(theme.Critical, target.Light);
        });
    }

    [Fact]
    public void Without_anything_to_send_it_a_web_button_does_not_wait()
    {
        Sta.Run(() =>
        {
            // As in the editor's preview or a snapshot: nothing takes the action.
            var widget = WidgetFactory.Create(Buttons(MovieNight()), Theme.From(new ThemeSettings()));
            var target = Assert.Single(((Panel)widget.Children[0]).Children.OfType<TapTarget>());
            target.Tap();
            Assert.False(target.Waiting);
            Assert.Null(target.Light);
        });
    }

    [Fact]
    public void A_tablet_can_press_a_web_button_and_sees_how_it_went()
    {
        Sta.Run(() =>
        {
            using var tablet = new TabletView { AllowTaps = true };
            var actions = new List<WidgetAction>();
            tablet.ActionRequested += (_, action) => actions.Add(action);
            tablet.Build(new LayoutDocument { Width = 400, Height = 200, Widgets = [Buttons(MovieNight())] }, new ThemeSettings());
            tablet.Draw(new MetricStore(), DateTime.Now);

            Assert.True(tablet.PressAt(0.5, 0.5));
            var action = Assert.Single(actions);
            Assert.Equal(WidgetAction.Web, action.Kind);
            Assert.NotNull(action.Done);
            action.Done!(true);
        });
    }

    [Fact]
    public void Imports_can_list_and_clear_where_web_buttons_send()
    {
        var layout = new LayoutDocument
        {
            Widgets =
            [
                Buttons(
                    MovieNight(),
                    new JsonObject { ["action"] = "web", ["method"] = "GET", ["target"] = " http://192.168.1.20/relay/0?turn=toggle " },
                    new JsonObject { ["action"] = "WEB" },
                    new JsonObject { ["action"] = "open", ["target"] = "calc.exe" }),
            ],
        };

        var targets = ButtonsWidget.WebTargets(layout);
        Assert.Equal(new[] { $"POST {ScriptAddress} (with a header)", "GET http://192.168.1.20/relay/0?turn=toggle" }, targets);
        Assert.All(targets, t => Assert.DoesNotContain("secret", t));

        ButtonsWidget.ClearWebTargets(layout);
        Assert.Empty(ButtonsWidget.WebTargets(layout));
        var movieNight = (JsonObject)((JsonArray)layout.Widgets[0]["buttons"]!)[0]!;
        Assert.Null(movieNight["header"]);
        Assert.Equal("""{"entity_id": "script.movie_night"}""", movieNight.GetString("body"));
        Assert.Equal(["calc.exe"], ButtonsWidget.OpenTargets(layout));
    }

    [Fact]
    public void Exported_layouts_leave_out_web_buttons_headers()
    {
        var layout = new LayoutDocument
        {
            Width = 400,
            Height = 200,
            Widgets = [Buttons(MovieNight(), new JsonObject { ["action"] = "web", ["target"] = "https://ntfy.sh/my-desk", ["body"] = "Dinner's ready" })],
        };
        using var package = new MemoryStream();
        var warnings = LayoutPackage.Write(layout, "Home", package);
        package.Position = 0;

        Assert.Equal(["Left out a web button's header, which can hold a password or token."], warnings);
        var buttons = (JsonArray)LayoutPackage.Read(package, "x").Layout.Widgets[0]["buttons"]!;
        Assert.Null(buttons[0]!["header"]);
        Assert.Equal(ScriptAddress, buttons[0]!.AsObject().GetString("target"));
        Assert.Equal("Dinner's ready", buttons[1]!.AsObject().GetString("body"));
        // The layout you keep still has it.
        Assert.Equal("Authorization: " + Token, ((JsonArray)layout.Widgets[0]["buttons"]!)[0]!.AsObject().GetString("header"));
    }
}
