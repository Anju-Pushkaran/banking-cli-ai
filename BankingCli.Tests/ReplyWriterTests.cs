using BankingCli.Ai;

namespace BankingCli.Tests;

public class ReplyWriterTests
{
    private sealed class FakeModel(string? reply) : ILanguageModel
    {
        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) =>
            reply is null ? throw new AiException("down") : Task.FromResult(reply);
    }

    private const string TransferLine = "TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00";

    private static async Task<string?> Explain(string? aiReply, string bankLine = TransferLine,
        string command = "transfer savings chequing 4")
    {
        Explanation result = await new ReplyWriter(new FakeModel(aiReply)).ExplainAsync("a request", command, bankLine);
        return result.Sentence;
    }

    [Fact]
    public async Task A_matching_sentence_is_returned()
    {
        string sentence = "Done! I moved $4 from savings to chequing. Savings now has $6.00 and chequing has $4.00.";

        Assert.Equal(sentence, await Explain(sentence));
    }

    [Theory]
    [InlineData("I moved $5 from savings to chequing. Savings has $6.00 and chequing has $4.00.")]  // wrong amount
    [InlineData("I moved $4 between your accounts. You now have $6.00 and $4.00.")]                  // accounts not named
    [InlineData("I moved $4 from savings to chequing. Savings has $6.00 and chequing has $4.00 plus $1 bonus.")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_sentence_that_does_not_match_the_bank_is_rejected(string sentence)
    {
        Assert.Null(await Explain(sentence));
    }

    [Fact]
    public async Task A_sentence_that_leaves_out_a_balance_is_rejected()
    {
        string line = "TRANSFER $4.00 FROM savings TO chequing | savings=$16.00 | chequing=$9.50";

        Assert.Null(await Explain("I moved $4 from savings to chequing. Savings now has $16.00.", line));
    }

    [Theory]
    [InlineData("I moved 40 dollars from savings to chequing. Savings now has $6.00 and chequing has $4.00.")]
    [InlineData("I moved $4 from savings to chequing. Savings now has 600 and chequing has $4.00.")]
    public async Task A_number_written_without_a_dollar_sign_must_still_match(string sentence)
    {
        Assert.Null(await Explain(sentence));
    }

    [Theory]
    [InlineData("I moved $4 from savings to chequing. Savings now has $4.00 and chequing has $6.00.")]  // swapped
    [InlineData("I moved $4 from savings to chequing. You now have $6.00 in chequing and $4.00 in savings.")]
    public async Task Balances_given_to_the_wrong_account_are_rejected(string sentence)
    {
        Assert.Null(await Explain(sentence));
    }

    [Theory]
    [InlineData("I moved $4 from savings to chequing. Savings now has $6.00 and chequing has $4.00.")]
    [InlineData("I moved $4 from savings to chequing. You now have $6.00 in savings and $4.00 in chequing.")]
    [InlineData("Done. Your new balances are $6.00 in savings and $4.00 in chequing, after moving $4 from savings to chequing.")]
    public async Task Balances_with_the_right_account_are_accepted_in_either_word_order(string sentence)
    {
        Assert.Equal(sentence, await Explain(sentence));
    }

    // Sentences the real model wrote during manual testing. The stricter check must not reject them.
    [Theory]
    [InlineData("The transfer of $4.00 from your savings to your chequing account was successful. Your new balances are $72.00 in savings and $59.50 in chequing.",
        "TRANSFER $4.00 FROM savings TO chequing | savings=$72.00 | chequing=$59.50")]
    [InlineData("Your deposit of $20.00 has been successfully made to your savings account, and your new balance is $92.00.",
        "DEPOSIT $20.00 TO savings | savings=$92.00")]
    [InlineData("Your chequing balance is $49.50 and your savings balance is $92.00.",
        "chequing=$49.50 | savings=$92.00")]
    [InlineData("The bank could not process your withdrawal of one million dollars from your chequing account due to insufficient funds. Your current balance in the chequing account is $184.50, and nothing was changed.",
        "ERROR INSUFFICIENT_FUNDS | chequing=$184.50")]
    public async Task Sentences_the_real_model_wrote_are_still_accepted(string sentence, string bankLine)
    {
        Assert.Equal(sentence, await Explain(sentence, bankLine));
    }

    [Theory]
    [InlineData("Done, transferred!")]
    [InlineData("Your transfer was a success.")]
    public async Task An_error_with_no_numbers_cannot_be_described_as_a_success(string sentence)
    {
        Assert.Null(await Explain(sentence, "ERROR SAME_ACCOUNT"));
    }

    [Fact]
    public async Task An_error_with_no_numbers_can_be_explained_as_a_refusal()
    {
        string sentence = "The transfer couldn't go through because both accounts are the same. Nothing was changed.";

        Assert.Equal(sentence, await Explain(sentence, "ERROR SAME_ACCOUNT"));
    }

    [Fact]
    public async Task A_refusal_may_repeat_the_amount_that_was_asked_for()
    {
        string sentence = "The bank couldn't withdraw $500 because chequing only has $4.00. Nothing was changed.";

        Assert.Equal(sentence,
            await Explain(sentence, "ERROR INSUFFICIENT_FUNDS | chequing=$4.00", "withdraw chequing 500"));
    }

    [Fact]
    public async Task A_number_that_is_not_in_the_bank_line_or_the_command_is_still_rejected()
    {
        string sentence = "The bank couldn't withdraw $600 because chequing only has $4.00. Nothing was changed.";

        Assert.Null(await Explain(sentence, "ERROR INSUFFICIENT_FUNDS | chequing=$4.00", "withdraw chequing 500"));
    }

    [Fact]
    public async Task A_rejected_sentence_is_kept_so_it_can_be_looked_at()
    {
        var writer = new ReplyWriter(new FakeModel("I moved $40 from savings to chequing."));

        Explanation result = await writer.ExplainAsync("a request", "transfer savings chequing 4", TransferLine);

        Assert.Null(result.Sentence);
        Assert.Equal("I moved $40 from savings to chequing.", result.Rejected);
    }

    [Fact]
    public async Task When_the_ai_is_down_nothing_is_returned()
    {
        Assert.Null(await Explain(null));
    }

    [Fact]
    public async Task A_refusal_can_be_explained()
    {
        string line = "ERROR INSUFFICIENT_FUNDS | chequing=$4.00";
        string sentence = "That withdrawal wasn't possible because chequing only has $4.00. Nothing was changed.";

        Assert.Equal(sentence, await Explain(sentence, line));
    }

    [Fact]
    public async Task A_refusal_with_no_amounts_cannot_gain_amounts()
    {
        Assert.Null(await Explain("Sorry, that failed. Try $50 instead.", "ERROR INVALID_ACCOUNT"));
    }
}

public class BankAssistantReplyTests
{
    // Answers the interpreter's request and the reply writer's request differently.
    private sealed class FakeModel(string intent, string? friendly) : ILanguageModel
    {
        public int FriendlyCalls { get; private set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            if (systemPrompt.Contains("structured banking request"))
            {
                return Task.FromResult(intent);
            }

            FriendlyCalls++;
            return friendly is null ? throw new AiException("down") : Task.FromResult(friendly);
        }
    }

    private const string TransferJson =
        """{"action":"transfer","account":"savings","destination":"chequing","amount":"4","unrecognised_account":null}""";

    private static (BankAssistant Assistant, BankSession Bank, FakeModel Model, StringWriter Notes) Create(
        string intent, string? friendly, bool showBankLine = false)
    {
        var bank = new BankSession();
        var model = new FakeModel(intent, friendly);
        var notes = new StringWriter();
        var assistant = new BankAssistant(bank, new IntentInterpreter(model), notes, new ReplyWriter(model), showBankLine);
        return (assistant, bank, model, notes);
    }

    private const string GoodSentence =
        "I moved $4 from savings to chequing. Savings now has $6.00 and chequing has $4.00.";

    [Fact]
    public async Task By_default_only_the_friendly_sentence_is_shown()
    {
        var (assistant, bank, _, _) = Create(TransferJson, GoodSentence);
        bank.Execute("deposit savings 10");

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal(GoodSentence, result.Output);
    }

    [Fact]
    public async Task With_the_option_on_the_bank_record_is_shown_under_the_sentence()
    {
        var (assistant, bank, _, _) = Create(TransferJson, GoodSentence, showBankLine: true);
        bank.Execute("deposit savings 10");

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.StartsWith("I moved $4 from savings", result.Output);
        Assert.EndsWith("Bank record: TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00",
            result.Output);
    }

    [Fact]
    public async Task When_the_reply_call_fails_the_bank_line_is_shown_and_the_command_is_not_run_again()
    {
        var (assistant, bank, _, notes) = Create(TransferJson, friendly: null);
        bank.Execute("deposit savings 10");

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal("TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00", result.Output);
        Assert.Equal("chequing=$4.00 | savings=$6.00", bank.Execute("balance").Output);  // moved once, not twice
        Assert.Contains("friendly reply", notes.ToString());
    }

    [Fact]
    public async Task The_rejected_reply_is_shown_in_the_notes_only_when_the_option_is_on()
    {
        var (quiet, bank1, _, quietNotes) = Create(TransferJson, "I moved $40 from savings to chequing.");
        bank1.Execute("deposit savings 10");
        await quiet.HandleAsync("Move four dollars from savings to chequing");

        var (verbose, bank2, _, verboseNotes) = Create(TransferJson, "I moved $40 from savings to chequing.",
            showBankLine: true);
        bank2.Execute("deposit savings 10");
        await verbose.HandleAsync("Move four dollars from savings to chequing");

        Assert.DoesNotContain("Rejected reply", quietNotes.ToString());
        Assert.Contains("Rejected reply: \"I moved $40 from savings to chequing.\"", verboseNotes.ToString());
    }

    [Fact]
    public async Task A_reply_with_wrong_numbers_is_replaced_by_the_bank_line()
    {
        var (assistant, bank, _, _) = Create(TransferJson, "I moved $40 from savings to chequing.");
        bank.Execute("deposit savings 10");

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.StartsWith("TRANSFER $4.00", result.Output);
    }

    [Fact]
    public async Task A_bank_refusal_is_explained_too()
    {
        var (assistant, _, _, _) = Create(
            """{"action":"withdraw","account":"chequing","amount":"50","unrecognised_account":null}""",
            "The withdrawal didn't go through because chequing only has $0.00. Nothing was changed.");

        CommandResult result = await assistant.HandleAsync("take out fifty dollars from chequing");

        Assert.StartsWith("The withdrawal didn't go through", result.Output);
        Assert.DoesNotContain("ERROR", result.Output);
    }

    [Fact]
    public async Task Original_commands_and_clarifications_never_ask_for_a_friendly_reply()
    {
        var (assistant, _, model, _) = Create(
            """{"action":"transfer","account":null,"destination":"savings","amount":"10","unrecognised_account":null}""",
            "unused");

        Assert.Equal("DEPOSIT $5.00 TO savings | savings=$5.00", (await assistant.HandleAsync("deposit savings 5")).Output);
        Assert.Contains("No changes were made", (await assistant.HandleAsync("Transfer 10 to my savings")).Output);
        Assert.Equal(0, model.FriendlyCalls);
    }
}
