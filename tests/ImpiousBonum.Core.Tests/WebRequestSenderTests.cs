using System.Net;

namespace ImpiousBonum.Core.Tests;

public sealed class WebRequestSenderTests
{
    [Theory]
    [InlineData("https://ntfy.sh/my-desk", true)]
    [InlineData(" http://homeassistant.local:8123/api/webhook/pc ", true)]
    [InlineData("homeassistant.local/api/webhook/pc", false)]
    [InlineData("ftp://example.com/", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("", false)]
    public void Only_http_and_https_addresses_are_sent_to(string address, bool usable) =>
        Assert.Equal(usable, WebRequestSender.Address(address) is not null);

    [Theory]
    [InlineData("http://localhost:8123/api/webhook/pc", true)]
    [InlineData("http://127.0.0.1:8123/api/webhook/pc", true)]
    [InlineData("http://[::1]:8123/api/webhook/pc", true)]
    [InlineData("http://homeassistant.local:8123/api/webhook/pc", false)]
    [InlineData("http://192.168.1.20/relay/0?turn=toggle", false)]
    [InlineData("https://ntfy.sh/my-desk", false)]
    public void Requests_to_this_pc_skip_the_proxy(string address, bool direct) =>
        Assert.Equal(direct, WebRequestSender.GoesDirect(new Uri(address)));

    [Fact]
    public async Task A_server_that_takes_too_long_is_given_up_on()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://homeassistant.local:8123/api/webhook/pc");
        var started = DateTime.UtcNow;
        var problem = await WebRequestSender.SendAsync(request, TimeSpan.FromMilliseconds(100), async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        Assert.Equal("homeassistant.local took too long to answer", problem);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task What_went_wrong_names_the_server_but_not_the_path_or_headers()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.com/hooks/secret-id?token=secret");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer secret");
        var problem = await WebRequestSender.SendAsync(request, TimeSpan.FromSeconds(5),
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { ReasonPhrase = "Forbidden" }));
        Assert.Equal("example.com answered 403 Forbidden", problem);
    }
}
