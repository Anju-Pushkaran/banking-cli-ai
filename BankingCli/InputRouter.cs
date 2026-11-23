namespace BankingCli;

// Decides if a line is an original command (bank) or plain English (AI).
internal static class InputRouter
{
    public static bool IsOriginalCommand(string input)
    {
        string[] tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return true; // blank line, the bank ignores it
        }

        // The account spot must hold a real account name.
        // That's how "deposit savings 10" (command) differs from "deposit 20 dollars" (English).
        // "deposit savings abc" is still a command, so the bank gives its usual error.
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
