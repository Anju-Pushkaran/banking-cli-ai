using BankingCli.Ai;

namespace BankingCli;

// Sits between the console and the bank.
// Original commands go straight to the bank. Plain English goes to the AI first,
// and only a checked command reaches the bank. Then the AI explains the result.
internal sealed class BankAssistant(BankSession bank, IntentInterpreter? interpreter, TextWriter notes,
    ReplyWriter? replyWriter = null, bool showBankLine = false)
{
    public async Task<CommandResult> HandleAsync(string input, CancellationToken cancellationToken = default)
    {
        if (interpreter is null || InputRouter.IsOriginalCommand(input))
        {
            return bank.Execute(input);
        }

        Interpretation interpretation = await interpreter.InterpretAsync(input, cancellationToken);
        switch (interpretation.Kind)
        {
            case InterpretationKind.Command:
                // The command runs once. It is never run again, even if the AI call fails afterwards.
                CommandResult result = bank.Execute(interpretation.Text);
                return await ExplainAsync(input, interpretation.Text, result, cancellationToken);

            case InterpretationKind.NeedsClarification:
                // Nothing goes to the bank, so no balance changes.
                return new CommandResult(interpretation.Text);

            default:
                // AI is down or gave nonsense, so act like the original app.
                notes.WriteLine("(The AI service is unavailable, so only the original commands work right now.)");
                return bank.Execute(input);
        }
    }

    // Show the friendly sentence (and the bank's line too, if asked). If that fails, show just the bank's line.
    private async Task<CommandResult> ExplainAsync(string input, string command, CommandResult result,
        CancellationToken cancellationToken)
    {
        if (replyWriter is null)
        {
            return result;
        }

        Explanation explanation = await replyWriter.ExplainAsync(input, command, result.Output, cancellationToken);
        if (explanation.Sentence is not { } friendly)
        {
            notes.WriteLine("(Could not write a friendly reply. Showing the bank's result.)");

            // For testing: show the AI's text to explain why it was rejected.
            if (showBankLine && explanation.Rejected is not null)
            {
                notes.WriteLine($"(Rejected reply: \"{explanation.Rejected}\")");
            }

            return result;
        }

        return showBankLine
            ? result with { Output = $"{friendly}{Environment.NewLine}Bank record: {result.Output}" }
            : result with { Output = friendly };
    }
}
