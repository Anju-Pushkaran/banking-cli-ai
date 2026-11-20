namespace BankingCli.Ai;

internal sealed record AiSettings(Uri BaseUrl, string Model, string ApiKey)
{
    public const string DefaultBaseUrl = "https://api.openai.com/v1";
    public const string DefaultModel = "gpt-4o-mini";

    // Null when there's no key, so the app can still run the old commands.
    public static AiSettings? FromEnvironment() => From(Environment.GetEnvironmentVariable);

    internal static AiSettings? From(Func<string, string?> getVariable)
    {
        string? key = getVariable("AI_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string baseUrl = NonEmpty(getVariable("AI_BASE_URL")) ?? DefaultBaseUrl;
        string model = NonEmpty(getVariable("AI_MODEL")) ?? DefaultModel;

        // Trailing slash needed, otherwise the /v1 part gets dropped when we add chat/completions.
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new AiException("AI_BASE_URL is not a valid http(s) address.");
        }

        return new AiSettings(uri, model, key.Trim());
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
