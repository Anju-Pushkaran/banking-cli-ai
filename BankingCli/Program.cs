using BankingCli;
using BankingCli.Ai;

BankSession banking = new BankSession();

IntentInterpreter? interpreter = null;
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
        interpreter = new IntentInterpreter(new OpenAiCompatibleModel(new HttpClient(), settings));
    }
}
catch (AiException ex)
{
    Console.Error.WriteLine($"Plain-English mode is off: {ex.Message}");
}

BankAssistant assistant = new BankAssistant(banking, interpreter, Console.Error);

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
