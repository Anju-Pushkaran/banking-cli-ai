using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace BankingCli.Ai;

//  HttpClient call to an OpenAI-style chat completions endpoint.
internal sealed class OpenAiCompatibleModel : ILanguageModel
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private readonly HttpClient http;
    private readonly AiSettings settings;

    public OpenAiCompatibleModel(HttpClient http, AiSettings settings)
    {
        this.http = http;
        this.settings = settings;
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            model = settings.Model,
            temperature = 0, // same sentence should give the same answer
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.BaseUrl, "chat/completions"))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                // Only the status code. The body could echo things we don't want to show.
                throw new AiException($"The AI service returned HTTP {(int)response.StatusCode}.");
            }

            string body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ExtractReply(body);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException("The AI service took too long to respond.");
        }
        catch (HttpRequestException ex)
        {
            throw new AiException("Could not reach the AI service.", ex);
        }
    }

    private static string ExtractReply(string responseBody)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(responseBody);
            string? text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new AiException("The AI service returned an empty reply.");
            }

            return text.Trim();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
                                       or IndexOutOfRangeException)
        {
            throw new AiException("The AI service returned a reply in an unexpected format.", ex);
        }
    }
}
