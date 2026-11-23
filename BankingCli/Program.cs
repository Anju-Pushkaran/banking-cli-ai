using BankingCli;
using BankingCli.Ai;

BankSession banking = new BankSession();

IntentInterpreter? interpreter = null;
ReplyWriter? replyWriter = null;
try
{
    AiSettings? settings = AiSettings.FromEnvironment();
    if (settings is null)
    {
        // stderr, so redirected output still only contains command results
        Console.Error.WriteLine("Plain-English mode is off (AI_API_KEY is not set). Original commands still work.");
    }
    else
    {
        var model = new OpenAiCompatibleModel(new HttpClient(), settings);
        interpreter = new IntentInterpreter(model);
        replyWriter = new ReplyWriter(model);
    }
}
catch (AiException ex)
{
    Console.Error.WriteLine($"Plain-English mode is off: {ex.Message}");
}

// Set SHOW_BANK_LINE=1 to also print the bank's own result under each friendly reply.
string? showLine = Environment.GetEnvironmentVariable("SHOW_BANK_LINE");
bool showBankLine = showLine == "1" || string.Equals(showLine, "true", StringComparison.OrdinalIgnoreCase);

BankAssistant assistant = new BankAssistant(banking, interpreter, Console.Error, replyWriter, showBankLine);

while (true)
{
    // Keep redirected output limited to command results so it can be compared line by line.
    if (!Console.IsInputRedirected)
    {
        Console.Write("bank> ");
    }

    string? input = Console.ReadLine();
    if (input is null)
    {
        break;
    }

    CommandResult result = await assistant.HandleAsync(input);
    if (result.Output.Length > 0)
    {
        Console.WriteLine(result.Output);
    }

    if (result.ShouldExit)
    {
        break;
    }
}
