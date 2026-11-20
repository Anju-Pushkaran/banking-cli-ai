using BankingCli.Ai;

namespace BankingCli.Tests;

public class InputRouterTests
{
    [Theory]
    [InlineData("deposit savings 10")]
    [InlineData("  WITHDRAW   Chequing 5.25 ")]
    [InlineData("transfer savings chequing 4")]
    [InlineData("balance")]
    [InlineData("balance savings")]
    [InlineData("help")]
    [InlineData("?")]
    [InlineData("exit")]
    [InlineData("")]
    [InlineData("deposit savings abc")]    // bad amount: still a command, bank gives its usual error
    [InlineData("transfer savings savings 4")]
    public void Original_commands_go_to_the_bank(string input)
    {
        Assert.True(InputRouter.IsOriginalCommand(input));
    }

    [Theory]
    [InlineData("Move four dollars from savings to chequing")]
    [InlineData("Transfer 10.20 to my savings account")]
    [InlineData("deposit 20 dollars")]
    [InlineData("transfer 10 to savings")]
    [InlineData("balance please")]
    [InlineData("deposit savings")]
    [InlineData("help me")]
    [InlineData("can you put 20 bucks in my savings?")]
    public void English_goes_to_the_ai(string input)
    {
        Assert.False(InputRouter.IsOriginalCommand(input));
    }
}

public class BankAssistantTests
{
    private sealed class FakeModel(string? reply) : ILanguageModel
    {
        public int Calls { get; private set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            Calls++;
            return reply is null ? throw new AiException("down") : Task.FromResult(reply);
        }
    }

    private static (BankAssistant Assistant, BankSession Bank, FakeModel? Model, StringWriter Notes) Create(
        string? aiReply, bool withAi = true)
    {
        var bank = new BankSession();
        var model = new FakeModel(aiReply);
        var notes = new StringWriter();
        var assistant = new BankAssistant(bank, withAi ? new IntentInterpreter(model) : null, notes);
        return (assistant, bank, model, notes);
    }

    private const string TransferJson =
        """{"action":"transfer","account":"savings","destination":"chequing","amount":"4","question":null}""";

    [Fact]
    public async Task Original_commands_never_call_the_ai()
    {
        var (assistant, _, model, _) = Create(TransferJson);

        CommandResult result = await assistant.HandleAsync("deposit savings 10");

        Assert.Equal("DEPOSIT $10.00 TO savings | savings=$10.00", result.Output);
        Assert.Equal(0, model!.Calls);
    }

    [Fact]
    public async Task Exit_still_ends_the_session()
    {
        var (assistant, _, _, _) = Create(TransferJson);

        Assert.True((await assistant.HandleAsync("exit")).ShouldExit);
    }

    [Fact]
    public async Task English_request_runs_the_validated_command_exactly_once()
    {
        var (assistant, bank, model, _) = Create(TransferJson);
        bank.Execute("deposit savings 10");

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal("TRANSFER $4.00 FROM savings TO chequing | savings=$6.00 | chequing=$4.00", result.Output);
        Assert.Equal("chequing=$4.00 | savings=$6.00", bank.Execute("balance").Output);
        Assert.Equal(1, model!.Calls);
    }

    [Fact]
    public async Task A_clarification_changes_no_balance()
    {
        var (assistant, bank, _, _) = Create(
            """{"action":"transfer","account":null,"destination":"savings","amount":"10.20","question":null}""");
        bank.Execute("deposit chequing 50");

        CommandResult result = await assistant.HandleAsync("Transfer 10.20 to my savings account");

        Assert.Contains("No changes were made", result.Output);
        Assert.Equal("chequing=$50.00 | savings=$0.00", bank.Execute("balance").Output);
    }

    [Fact]
    public async Task Insufficient_funds_is_left_to_the_bank()
    {
        var (assistant, bank, _, _) = Create(
            """{"action":"withdraw","account":"chequing","destination":null,"amount":"50","question":null}""");
        bank.Execute("deposit chequing 4");

        CommandResult result = await assistant.HandleAsync("take out fifty dollars from chequing");

        Assert.Equal("ERROR INSUFFICIENT_FUNDS | chequing=$4.00", result.Output);
    }

    [Fact]
    public async Task When_the_ai_is_down_the_bank_answers_as_it_always_did()
    {
        var (assistant, bank, _, notes) = Create(aiReply: null);

        CommandResult result = await assistant.HandleAsync("Move four dollars from savings to chequing");

        Assert.Equal("ERROR UNKNOWN_COMMAND", result.Output);
        Assert.Contains("unavailable", notes.ToString());
        Assert.Equal("chequing=$0.00 | savings=$0.00", bank.Execute("balance").Output);
    }

    [Fact]
    public async Task Without_an_ai_everything_behaves_like_the_original_app()
    {
        var (assistant, _, _, notes) = Create(TransferJson, withAi: false);

        Assert.Equal("ERROR UNKNOWN_COMMAND", (await assistant.HandleAsync("move money please")).Output);
        Assert.Equal("chequing=$0.00 | savings=$0.00", (await assistant.HandleAsync("balance")).Output);
        Assert.Empty(notes.ToString());
    }
}
