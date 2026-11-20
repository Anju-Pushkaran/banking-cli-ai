using BankingCli.Ai;

namespace BankingCli.Tests;

public class IntentInterpreterTests
{
    // Fake AI: returns a fixed reply, or fails when reply is null.
    private sealed class FakeModel(string? reply) : ILanguageModel
    {
        public string? LastUserPrompt { get; private set; }

        public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        {
            LastUserPrompt = userPrompt;
            return reply is null ? throw new AiException("down") : Task.FromResult(reply);
        }
    }

    private static Task<Interpretation> Run(string? aiReply) =>
        new IntentInterpreter(new FakeModel(aiReply)).InterpretAsync("anything");

    private static string Json(string action, string? account = null, string? destination = null,
        string? amount = null, string? question = null)
    {
        static string V(string? s) => s is null ? "null" : $"\"{s}\"";
        return $$"""{"action":"{{action}}","account":{{V(account)}},"destination":{{V(destination)}},"amount":{{V(amount)}},"question":{{V(question)}}}""";
    }

    [Theory]
    [InlineData("deposit", "savings", null, "20", "deposit savings 20")]
    [InlineData("withdraw", "chequing", null, "4.50", "withdraw chequing 4.50")]
    [InlineData("transfer", "savings", "chequing", "4", "transfer savings chequing 4")]
    [InlineData("transfer", "Savings", "CHEQUING", "10.20", "transfer savings chequing 10.20")]
    public async Task Complete_requests_become_exact_commands(string action, string account, string? destination,
        string amount, string expected)
    {
        Interpretation result = await Run(Json(action, account, destination, amount));

        Assert.Equal(InterpretationKind.Command, result.Kind);
        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public async Task Balance_works_for_both_accounts_or_one()
    {
        Assert.Equal("balance", (await Run(Json("balance"))).Text);
        Assert.Equal("balance savings", (await Run(Json("balance", "savings"))).Text);
    }

    [Fact]
    public async Task Balance_for_an_unknown_account_asks_instead_of_showing_both()
    {
        Interpretation result = await Run(Json("balance", "visa"));

        Assert.Equal(InterpretationKind.NeedsClarification, result.Kind);
    }

    [Fact]
    public async Task Transfer_with_no_source_asks_and_does_not_guess()
    {
        // "Transfer 10.20 to my savings account": we know the destination and amount, not the source.
        Interpretation result = await Run(Json("transfer", null, "savings", "10.20"));

        Assert.Equal(InterpretationKind.NeedsClarification, result.Kind);
        Assert.Contains("try again", result.Text);
    }

    [Theory]
    [InlineData("deposit", null, "20")]       // no account
    [InlineData("withdraw", "savings", null)] // no amount
    public async Task Deposit_or_withdraw_missing_a_detail_asks(string action, string? account, string? amount)
    {
        Interpretation result = await Run(Json(action, account, null, amount));

        Assert.Equal(InterpretationKind.NeedsClarification, result.Kind);
    }

    [Fact]
    public async Task Transfer_missing_the_amount_asks()
    {
        Assert.Equal(InterpretationKind.NeedsClarification,
            (await Run(Json("transfer", "savings", "chequing"))).Kind);
    }

    [Fact]
    public async Task The_models_own_clarify_question_is_shown()
    {
        Interpretation result = await Run(Json("clarify", question: "How much, and from which account?"));

        Assert.Equal(InterpretationKind.NeedsClarification, result.Kind);
        Assert.Equal("How much, and from which account?", result.Text);
    }

    [Fact]
    public async Task Clarify_without_a_question_gets_a_default_message()
    {
        Interpretation result = await Run(Json("clarify"));

        Assert.Equal(InterpretationKind.NeedsClarification, result.Kind);
        Assert.NotEmpty(result.Text);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("1.234")]
    [InlineData("a lot")]
    [InlineData("1,000")]
    [InlineData("$20")]
    [InlineData("1e3")]
    [InlineData("20; exit")]
    public async Task Bad_amounts_never_become_commands(string amount)
    {
        Interpretation result = await Run(Json("deposit", "savings", null, amount));

        Assert.NotEqual(InterpretationKind.Command, result.Kind);
    }

    [Fact]
    public async Task Unknown_or_same_accounts_are_rejected()
    {
        Assert.NotEqual(InterpretationKind.Command, (await Run(Json("deposit", "visa", null, "5"))).Kind);
        Assert.NotEqual(InterpretationKind.Command, (await Run(Json("transfer", "savings", "savings", "5"))).Kind);
    }

    [Fact]
    public async Task A_bare_json_number_for_amount_is_accepted()
    {
        Interpretation result = await Run("""{"action":"deposit","account":"savings","amount":20}""");

        Assert.Equal("deposit savings 20", result.Text);
    }

    [Fact]
    public async Task Json_wrapped_in_code_fences_or_chatter_is_still_read()
    {
        string wrapped = "Sure! ```json\n" + Json("deposit", "savings", null, "5") + "\n```";

        Assert.Equal("deposit savings 5", (await Run(wrapped)).Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"action":"delete_everything"}""")]
    public async Task Garbage_from_the_model_is_never_acted_on(string reply)
    {
        Assert.Equal(InterpretationKind.Unavailable, (await Run(reply)).Kind);
    }

    [Fact]
    public async Task When_the_ai_is_down_the_result_is_unavailable()
    {
        Interpretation result = await Run(null);

        Assert.Equal(InterpretationKind.Unavailable, result.Kind);
    }

    [Fact]
    public async Task The_users_sentence_is_passed_to_the_model_unchanged()
    {
        var model = new FakeModel(Json("balance"));

        await new IntentInterpreter(model).InterpretAsync("what do I have in savings?");

        Assert.Equal("what do I have in savings?", model.LastUserPrompt);
    }
}
