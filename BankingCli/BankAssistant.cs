using BankingCli.Ai;

namespace BankingCli;

// Sits between the console and the bank. Original commands go straight through.
// Plain English is interpreted by the AI first, and only a validated command reaches the bank.
internal sealed class BankAssistant(BankSession bank, IntentInterpreter? interpreter, TextWriter notes)
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
                return bank.Execute(interpretation.Text);

            case InterpretationKind.NeedsClarification:
                // Nothing is sent to the bank, so no balance can change.
                return new CommandResult(interpretation.Text);

            default:
                // AI down or unusable reply: behave like the original app and let the bank answer.
                notes.WriteLine("(Plain-English mode is unavailable. Your text was sent to the bank as typed.)");
                return bank.Execute(input);
        }
    }
}
