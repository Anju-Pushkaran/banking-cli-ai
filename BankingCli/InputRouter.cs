namespace BankingCli;

// Decides whether a line is an original command (goes straight to the bank) or plain English (goes to the AI).
internal static class InputRouter
{
    public static bool IsOriginalCommand(string input)
    {
        string[] tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true; // blank line, the bank ignores it
        }

        // Account slots must hold a real account name. That's what separates
        // "deposit savings 10" (command) from "deposit 20 dollars" (English).
        // A bad amount like "deposit savings abc" still counts as a command, so it gets the same error as before.
        return tokens[0].ToLowerInvariant() switch
        {
            "help" or "?" or "exit" => tokens.Length == 1,
            "balance" => tokens.Length == 1 || (tokens.Length == 2 && IsAccount(tokens[1])),
            "deposit" or "withdraw" => tokens.Length == 3 && IsAccount(tokens[1]),
            "transfer" => tokens.Length == 4 && IsAccount(tokens[1]) && IsAccount(tokens[2]),
            _ => false
        };
    }

    private static bool IsAccount(string token) =>
        token.ToLowerInvariant() is "chequing" or "savings";
}
