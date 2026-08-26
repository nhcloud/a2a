using Microsoft.AspNetCore.Http;

namespace A2A.Demo.HostedAgent.Controllers;

/// <summary>
/// Prints one line per inbound A2A call, naming the console demo that produces it.
/// </summary>
/// <remarks>
/// The whole reason the protocol surface is a controller rather than a minimal-API
/// lambda: every request the console makes lands on a named method you can breakpoint,
/// and the log says which of the five demos put it there.
/// </remarks>
internal static class DemoTrace
{
    /// <summary>
    /// A2A JSON-RPC method → the console demo(s) that call it.
    /// Keys are the A2A 1.0 method names (PascalCase, not the pre-1.0 "message/send" form).
    /// </summary>
    private static readonly Dictionary<string, string> Callers = new(StringComparer.OrdinalIgnoreCase)
    {
        [A2AMethods.SendMessage] =
            "demo 2 'ask' (one per turn) · demo 4 'job' (the start, with returnImmediately) · demo 5 'delegate' (the tool call)",
        [A2AMethods.SendStreamingMessage] =
            "demo 3 'stream' (SSE: the response stays open for the whole job)",
        [A2AMethods.GetTask] =
            "demo 4 'job' (every continuation-token poll, then the raw tasks/get at the end)",
        [A2AMethods.SubscribeToTask] =
            "no demo calls this — it is how a second client would attach to a task already running",
        [A2AMethods.CancelTask] =
            "no demo calls this — Ctrl+C during a job would",
        [A2AMethods.ListTasks] =
            "no demo calls this",
        [A2AMethods.GetExtendedAgentCard] =
            "no demo calls this — it is the authenticated card, for callers that get to see more",
    };

    /// <summary>Logs a JSON-RPC call against the demo that produces it.</summary>
    public static void JsonRpc(ILogger logger, string method, JsonRpcId id, HttpContext http)
    {
        logger.LogInformation(
            "→ POST {Path}  {Method}  (id {Id})  ← {Caller}",
            http.Request.Path.Value,
            method,
            id.HasValue ? id.ToString() : "(none)",
            Callers.TryGetValue(method, out string? caller) ? caller : "unknown method");
    }

    /// <summary>Logs a plain HTTP call (the Agent Card) against the demo that produces it.</summary>
    public static void Http(ILogger logger, HttpContext http, string caller)
    {
        logger.LogInformation(
            "→ {Verb} {Path}  ← {Caller}",
            http.Request.Method,
            http.Request.Path.Value,
            caller);
    }
}
