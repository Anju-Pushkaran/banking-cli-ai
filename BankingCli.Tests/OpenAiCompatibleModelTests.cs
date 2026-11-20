using System.Net;
using System.Text;
using System.Text.Json;
using BankingCli.Ai;

namespace BankingCli.Tests;

public class OpenAiCompatibleModelTests
{
    private static readonly AiSettings Settings =
        new(new Uri("https://example.test/v1/"), "test-model", "secret-key-123");

    // Fake network: returns what we give it and remembers the request it got.
    private sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return await respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (OpenAiCompatibleModel Model, FakeHandler Handler) Create(HttpResponseMessage response)
    {
        var handler = new FakeHandler(_ => Task.FromResult(response));
        return (new OpenAiCompatibleModel(new HttpClient(handler), Settings), handler);
    }

    [Fact]
    public async Task Sends_expected_request_and_returns_reply_text()
    {
        var (model, handler) = Create(Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"  ready  "}}]}"""));

        string reply = await model.CompleteAsync("be brief", "say ready");

        Assert.Equal("ready", reply);
        Assert.Equal("https://example.test/v1/chat/completions", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key-123", handler.LastRequest.Headers.Authorization.Parameter);

        using JsonDocument body = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("test-model", body.RootElement.GetProperty("model").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("temperature").GetInt32());
        JsonElement messages = body.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("be brief", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("say ready", messages[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_errors_become_AiException_without_leaking_the_key_or_body(HttpStatusCode status)
    {
        var (model, _) = Create(Json(status, """{"error":"secret-key-123 is bad"}"""));

        AiException ex = await Assert.ThrowsAsync<AiException>(() => model.CompleteAsync("s", "u"));

        Assert.Contains(((int)status).ToString(), ex.Message);
        Assert.DoesNotContain("secret-key-123", ex.Message);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"choices":[]}""")]
    [InlineData("""{"choices":[{"message":{"content":null}}]}""")]
    [InlineData("""{"choices":[{"message":{"content":"   "}}]}""")]
    public async Task Unusable_replies_become_AiException(string body)
    {
        var (model, _) = Create(Json(HttpStatusCode.OK, body));

        await Assert.ThrowsAsync<AiException>(() => model.CompleteAsync("s", "u"));
    }

    [Fact]
    public async Task Network_failure_becomes_AiException()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("no network"));
        var model = new OpenAiCompatibleModel(new HttpClient(handler), Settings);

        await Assert.ThrowsAsync<AiException>(() => model.CompleteAsync("s", "u"));
    }
}

public class AiSettingsTests
{
    private static AiSettings? Load(params (string Name, string Value)[] vars) =>
        AiSettings.From(name => vars.FirstOrDefault(v => v.Name == name).Value);

    [Fact]
    public void No_key_means_no_settings()
    {
        Assert.Null(Load());
        Assert.Null(Load(("AI_API_KEY", "   ")));
    }

    [Fact]
    public void Defaults_are_used_when_only_the_key_is_set()
    {
        AiSettings s = Load(("AI_API_KEY", " k "))!;

        Assert.Equal("k", s.ApiKey);
        Assert.Equal("gpt-4o-mini", s.Model);
        Assert.Equal("https://api.openai.com/v1/", s.BaseUrl.ToString());
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://localhost:11434/v1/")]
    public void Custom_base_url_keeps_its_path_with_or_without_trailing_slash(string url)
    {
        AiSettings s = Load(("AI_API_KEY", "k"), ("AI_BASE_URL", url), ("AI_MODEL", "llama3"))!;

        Assert.Equal("llama3", s.Model);
        Assert.Equal("http://localhost:11434/v1/chat/completions", new Uri(s.BaseUrl, "chat/completions").ToString());
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.com/v1")]
    public void Invalid_base_url_is_rejected(string url)
    {
        Assert.Throws<AiException>(() => Load(("AI_API_KEY", "k"), ("AI_BASE_URL", url)));
    }
}
