namespace BankingCli.Ai;

internal interface ILanguageModel
{
    // Any failure comes back as an AiException.
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}

internal sealed class AiException(string message, Exception? inner = null) : Exception(message, inner);
