using A2A.Demo.Client.Factories;
using A2A.Demo.Client.Infrastructure;
using Microsoft.Agents.AI;
using System.Diagnostics;

namespace A2A.Demo.Client.Demos;

/// <summary>
/// Server-sent events: the same long-running job as the polling demo, delivered live
/// instead of on request.
/// </summary>
/// <remarks>
/// <para>Talk slide 10 — streaming (SSE), one of the three result-delivery patterns.
/// Run this back to back with the polling demo: identical work on the server, two
/// very different caller experiences, and nothing changed but the method name.</para>
/// <para>Every update carries the protocol event it came from on
/// <see cref="AgentResponseUpdate.RawRepresentation"/>, so this demo can show both
/// layers at once: the tidy Agent Framework stream, and the A2A status and artifact
/// events underneath it.</para>
/// </remarks>
public sealed class StreamingDemo : IDemoScenario
{
    private const string Job = "Research the market for AI agent interoperability tooling.";

    private readonly AgentFactoryProvider _factories;

    public StreamingDemo(AgentFactoryProvider factories) => _factories = factories;

    public string Key => "stream";

    public string Title => "Streaming — watch a long job report progress live";

    public string Summary => "SSE: task status transitions and artifact chunks as they happen.";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        AIAgent agent = await _factories.CreateAsync("a2a", cancellationToken).ConfigureAwait(false);
        AgentSession session = await agent.CreateSessionAsync(cancellationToken).ConfigureAwait(false);

        Ux.Prompt(Job);
        Ux.Step("RunStreamingAsync — hold the connection open and take updates as they land.");
        Ux.Step("« lines are the raw A2A events; white lines are what application code sees.");
        Console.WriteLine();

        var stopwatch = Stopwatch.StartNew();
        int events = 0;
        int chunks = 0;
        int characters = 0;

        await foreach (AgentResponseUpdate update in agent
            .RunStreamingAsync(Job, session, cancellationToken: cancellationToken)
            .WithCancellation(cancellationToken)
            .ConfigureAwait(false))
        {
            events++;
            string at = $"[{stopwatch.Elapsed.TotalSeconds,5:F1}s]";

            switch (update.RawRepresentation)
            {
                // The task itself, sent first so the caller has an id to hold on to.
                case AgentTask task:
                    Ux.Wire($"{at} Task {task.Id} created — state {task.Status?.State}");
                    break;

                // Lifecycle transitions. These carry progress text but no report content.
                case TaskStatusUpdateEvent status:
                    string? note = status.Status?.Message?.Parts?.FirstOrDefault()?.Text;
                    Ux.Wire($"{at} TaskStatusUpdate → {status.Status?.State}"
                          + (string.IsNullOrWhiteSpace(note) ? string.Empty : $" · {note}"));
                    break;

                // The report, arriving in pieces as the remote agent writes it.
                case TaskArtifactUpdateEvent artifact:
                    chunks++;
                    characters += update.Text.Length;
                    Ux.Wire($"{at} TaskArtifactUpdate → \"{artifact.Artifact?.ArtifactId}\" "
                          + $"append={artifact.Append} lastChunk={artifact.LastChunk}");
                    Ux.WriteLine($"          chunk {chunks}: {FirstLine(update.Text)} "
                               + $"(+{update.Text.Length} chars)", ConsoleColor.White);
                    break;

                default:
                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        chunks++;
                        characters += update.Text.Length;
                        Ux.WriteLine($"  {at} {FirstLine(update.Text)}", ConsoleColor.White);
                    }
                    break;
            }
        }

        stopwatch.Stop();
        Console.WriteLine();

        Ux.Step($"{events} protocol events | {chunks} carried report content | {characters} chars | "
              + $"{stopwatch.Elapsed.TotalSeconds:F1}s");
        Ux.Success("Same job as the polling demo. The caller just chose to stay on the line.");
    }

    private static string FirstLine(string text)
    {
        string line = text
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;

        return line.Length <= 60 ? line : line[..60] + "…";
    }
}
