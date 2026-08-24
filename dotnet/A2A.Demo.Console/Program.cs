using A2A.Demo.Client.Demos;
using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;

IConfiguration configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(typeof(Program).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();

services.AddLogging(logging =>
{
    logging.AddConsole();
    // Quiet by default so the demo output is readable. Set Logging:LogLevel:Default
    // to Information in appsettings.json to show the protocol chatter instead.
    logging.SetMinimumLevel(
        Enum.TryParse(configuration["Logging:LogLevel:Default"], out LogLevel level)
            ? level
            : LogLevel.Warning);
});

services.Configure<A2AOptions>(configuration.GetSection(A2AOptions.SectionName));
services.Configure<AzureOpenAIOptions>(configuration.GetSection(AzureOpenAIOptions.SectionName));

// ── The factories ───────────────────────────────────────────────────────────────
// Both produce an AIAgent. One is a remote agent behind the A2A protocol, the other
// runs in this process against Azure OpenAI. The demos only ever see AIAgent.
services.AddSingleton<A2AAgentFactory>();
services.AddSingleton<AzureOpenAIAgentFactory>();
services.AddSingleton<IAgentFactory>(sp => sp.GetRequiredService<A2AAgentFactory>());
services.AddSingleton<IAgentFactory>(sp => sp.GetRequiredService<AzureOpenAIAgentFactory>());
services.AddSingleton<AgentFactoryProvider>();

// ── The demos ───────────────────────────────────────────────────────────────────
services.AddSingleton<IDemoScenario, DiscoveryDemo>();
services.AddSingleton<IDemoScenario, RequestResponseDemo>();
services.AddSingleton<IDemoScenario, StreamingDemo>();
services.AddSingleton<IDemoScenario, LongRunningJobDemo>();
services.AddSingleton<IDemoScenario, DelegationDemo>();

await using ServiceProvider provider = services.BuildServiceProvider();

var scenarios = provider.GetRequiredService<IEnumerable<IDemoScenario>>().ToList();
var factories = provider.GetRequiredService<AgentFactoryProvider>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Ux.Banner(
    "A2A + Microsoft Agent Framework",
    "Calling a remote agent as if it were local. .NET 10 · Agent Framework 1.19.0");

Ux.Heading("Registered agent factories");
foreach (IAgentFactory factory in factories.All)
{
    if (factory.IsConfigured)
    {
        Ux.Success($"{factory.Key,-14} {factory.DisplayName}");
    }
    else
    {
        Ux.Warn($"{factory.Key,-14} not configured — {factory.ConfigurationHint}");
    }
}

// Non-interactive mode: pass demo keys as arguments, or "all".
string[] requested = args.Length > 0
    ? (args[0].Equals("all", StringComparison.OrdinalIgnoreCase)
        ? [.. scenarios.Select(s => s.Key)]
        : args)
    : [];

if (requested.Length > 0)
{
    foreach (string key in requested)
    {
        await RunAsync(key).ConfigureAwait(false);
    }

    return 0;
}

// Interactive menu.
while (!cts.IsCancellationRequested)
{
    Ux.Heading("Demos");
    for (int i = 0; i < scenarios.Count; i++)
    {
        Ux.WriteLine($"  {i + 1}. {scenarios[i].Title}", ConsoleColor.White);
        Ux.Info($"     {scenarios[i].Summary}");
    }
    Ux.WriteLine("  q. quit", ConsoleColor.White);
    Console.WriteLine();
    Ux.Write("  select → ", ConsoleColor.Cyan);

    string? input = Console.ReadLine()?.Trim();
    if (string.IsNullOrEmpty(input) || input.Equals("q", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    IDemoScenario? chosen = int.TryParse(input, out int index) && index >= 1 && index <= scenarios.Count
        ? scenarios[index - 1]
        : scenarios.FirstOrDefault(s => s.Key.Equals(input, StringComparison.OrdinalIgnoreCase));

    if (chosen is null)
    {
        Ux.Error($"No demo matches '{input}'.");
        continue;
    }

    await RunAsync(chosen.Key).ConfigureAwait(false);
}

return 0;

async Task RunAsync(string key)
{
    IDemoScenario? scenario = scenarios.FirstOrDefault(
        s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    if (scenario is null)
    {
        Ux.Error($"Unknown demo '{key}'. Known: {string.Join(", ", scenarios.Select(s => s.Key))}.");
        return;
    }

    Ux.Banner(scenario.Title, scenario.Summary);

    try
    {
        await scenario.RunAsync(cts.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        Ux.Warn("Canceled.");
    }
    catch (HttpRequestException ex)
    {
        Ux.Error($"Could not reach the remote agent: {ex.Message}");
        Ux.Info("Start it with:  dotnet run --project dotnet/A2A.Demo.HostedAgent");
    }
    catch (Exception ex)
    {
        Ux.Error($"{ex.GetType().Name}: {ex.Message}");
    }
}
