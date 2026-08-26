using A2A.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace A2A.Demo.HostedAgent.Controllers;

/// <summary>
/// The A2A JSON-RPC surface, written out as a controller instead of
/// <c>app.MapA2A("/a2a")</c>.
/// </summary>
/// <remarks>
/// <para><c>MapA2A</c> does exactly what this file does — parse the JSON-RPC envelope,
/// switch on <c>method</c>, call <see cref="IA2ARequestHandler"/>, and write either a
/// single JSON response or an SSE stream. Spelling it out costs about eighty lines and
/// buys the demo two things: a named method per protocol call to breakpoint, and a log
/// line that says which of the five console demos triggered it.</para>
/// <para>The A2A server itself is unchanged. <c>AddA2AAgent&lt;DemoAgentHandler&gt;</c>
/// still owns the task store, the event notifier and the lifecycle; this controller is
/// only the transport in front of it.</para>
/// <para>Note the method names: A2A 1.0 uses <c>SendMessage</c> / <c>GetTask</c>, not the
/// pre-1.0 <c>message/send</c> / <c>tasks/get</c> that older samples show.</para>
/// </remarks>
[ApiController]
[Route("a2a")]
public sealed class A2AController : ControllerBase
{
    private readonly IA2ARequestHandler _handler;
    private readonly ILogger<A2AController> _logger;

    public A2AController(IA2ARequestHandler handler, ILogger<A2AController> logger)
    {
        _handler = handler;
        _logger = logger;
    }

    /// <summary>
    /// Every A2A call the console makes arrives here: <c>POST /a2a</c>, one JSON-RPC
    /// envelope, the <c>method</c> field deciding what happens next.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> PostAsync(CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;

        try
        {
            // Deserialized by hand with the SDK's own options: A2A types use custom
            // converters (the Part and StreamResponse unions especially), and MVC's
            // default input formatter does not know about them.
            request = await JsonSerializer
                .DeserializeAsync<JsonRpcRequest>(Request.Body, A2AJsonUtilities.DefaultOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            return JsonRpc(JsonRpcResponse.ParseErrorResponse(default, ex.Message));
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            return JsonRpc(JsonRpcResponse.InvalidRequestResponse(
                request?.Id ?? default, "Missing JSON-RPC 'method'."));
        }

        DemoTrace.JsonRpc(_logger, request.Method, request.Id, HttpContext);

        try
        {
            return request.Method switch
            {
                A2AMethods.SendMessage => await SendMessageAsync(request, cancellationToken).ConfigureAwait(false),
                A2AMethods.SendStreamingMessage => SendStreamingMessage(request),
                A2AMethods.GetTask => await GetTaskAsync(request, cancellationToken).ConfigureAwait(false),
                A2AMethods.ListTasks => await ListTasksAsync(request, cancellationToken).ConfigureAwait(false),
                A2AMethods.CancelTask => await CancelTaskAsync(request, cancellationToken).ConfigureAwait(false),
                A2AMethods.SubscribeToTask => SubscribeToTask(request),
                A2AMethods.GetExtendedAgentCard => await GetExtendedAgentCardAsync(request, cancellationToken).ConfigureAwait(false),

                // The Agent Card advertises pushNotifications: false, so the four
                // push-config methods get the protocol's own "not supported" error
                // rather than a 404 or an exception.
                A2AMethods.CreateTaskPushNotificationConfig or
                A2AMethods.GetTaskPushNotificationConfig or
                A2AMethods.ListTaskPushNotificationConfig or
                A2AMethods.DeleteTaskPushNotificationConfig =>
                    JsonRpc(JsonRpcResponse.PushNotificationNotSupportedResponse(
                        request.Id, $"This agent does not support {request.Method}.")),

                _ => JsonRpc(JsonRpcResponse.MethodNotFoundResponse(
                    request.Id, $"Unknown A2A method '{request.Method}'.")),
            };
        }
        catch (A2AException ex)
        {
            // Protocol-shaped failures carry their own error code (task not found,
            // not cancelable, ...) and must come back as JSON-RPC errors, not 500s.
            return JsonRpc(JsonRpcResponse.CreateJsonRpcErrorResponse(request.Id, ex));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unhandled failure in {Method}", request.Method);
            return JsonRpc(JsonRpcResponse.InternalErrorResponse(request.Id, ex.Message));
        }
    }

    // ── One method per A2A verb ─────────────────────────────────────────────────────
    // [NonAction] because they are not separately routable — JSON-RPC multiplexes all
    // of them onto one POST. They are still the place to put a breakpoint.

    /// <summary>
    /// <c>SendMessage</c> — demos 2 ("ask"), 4 ("job", the start) and 5 ("delegate").
    /// Returns a Message when the agent could answer immediately, a Task when it could not.
    /// </summary>
    [NonAction]
    public async Task<IActionResult> SendMessageAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = ParamsAs<SendMessageRequest>(request);

        _logger.LogInformation(
            "   SendMessage | returnImmediately {ReturnImmediately} | text \"{Text}\"",
            payload.Configuration?.ReturnImmediately ?? false,
            Preview(payload.Message));

        SendMessageResponse response = await _handler
            .SendMessageAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "   ← {Case}{Detail}",
            response.PayloadCase,
            response.Task is { } task ? $" id {task.Id} state {task.Status?.State}" : string.Empty);

        return JsonRpcOk(request.Id, response);
    }

    /// <summary>
    /// <c>SendStreamingMessage</c> — demo 3 ("stream"). The response is SSE, so it is
    /// written straight to the socket and the action returns before the job finishes.
    /// </summary>
    [NonAction]
    public IActionResult SendStreamingMessage(JsonRpcRequest request)
    {
        var payload = ParamsAs<SendMessageRequest>(request);

        _logger.LogInformation("   SendStreamingMessage | text \"{Text}\"", Preview(payload.Message));

        // RequestAborted, not the action's token: the enumeration outlives this method.
        return new HttpResultActionResult(new JsonRpcStreamedResult(
            _handler.SendStreamingMessageAsync(payload, HttpContext.RequestAborted),
            request.Id));
    }

    /// <summary>
    /// <c>GetTask</c> — demo 4 ("job"): every continuation-token poll, then once more
    /// at the end when the demo drops to the raw protocol to show the Task on the wire.
    /// </summary>
    [NonAction]
    public async Task<IActionResult> GetTaskAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = ParamsAs<GetTaskRequest>(request);

        AgentTask task = await _handler.GetTaskAsync(payload, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "   GetTask {TaskId} | state {State} | {Artifacts} artifact(s)",
            payload.Id, task.Status?.State, task.Artifacts?.Count ?? 0);

        return JsonRpcOk(request.Id, task);
    }

    /// <summary><c>ListTasks</c> — no demo calls this; it is here to complete the surface.</summary>
    [NonAction]
    public async Task<IActionResult> ListTasksAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = ParamsAs<ListTasksRequest>(request);
        ListTasksResponse response = await _handler.ListTasksAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonRpcOk(request.Id, response);
    }

    /// <summary><c>CancelTask</c> — reaches <see cref="Agents.DemoAgentHandler.CancelAsync"/>.</summary>
    [NonAction]
    public async Task<IActionResult> CancelTaskAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = ParamsAs<CancelTaskRequest>(request);
        AgentTask task = await _handler.CancelTaskAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonRpcOk(request.Id, task);
    }

    /// <summary>
    /// <c>SubscribeToTask</c> — SSE again, but for a task that is already running.
    /// This is how a second client picks up work the first one started.
    /// </summary>
    [NonAction]
    public IActionResult SubscribeToTask(JsonRpcRequest request)
    {
        var payload = ParamsAs<SubscribeToTaskRequest>(request);
        return new HttpResultActionResult(new JsonRpcStreamedResult(
            _handler.SubscribeToTaskAsync(payload, HttpContext.RequestAborted),
            request.Id));
    }

    /// <summary><c>GetExtendedAgentCard</c> — the card an authenticated caller gets to see.</summary>
    [NonAction]
    public async Task<IActionResult> GetExtendedAgentCardAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var payload = ParamsAs<GetExtendedAgentCardRequest>(request);
        AgentCard card = await _handler.GetExtendedAgentCardAsync(payload, cancellationToken).ConfigureAwait(false);
        return JsonRpcOk(request.Id, card);
    }

    // ── Plumbing ────────────────────────────────────────────────────────────────────

    /// <summary>Deserializes the JSON-RPC <c>params</c> member into the request type A2A defines for it.</summary>
    private static T ParamsAs<T>(JsonRpcRequest request) where T : new() =>
        request.Params is { } parameters
            ? parameters.Deserialize<T>(A2AJsonUtilities.DefaultOptions) ?? new T()
            : new T();

    /// <summary>Wraps a result in the JSON-RPC envelope, serialized with the SDK's converters.</summary>
    private static IActionResult JsonRpcOk<T>(JsonRpcId id, T result) =>
        JsonRpc(JsonRpcResponse.CreateJsonRpcResponse(
            id, result, (JsonTypeInfo<T>)A2AJsonUtilities.DefaultOptions.GetTypeInfo(typeof(T))));

    /// <summary>
    /// Hands a minimal-API <see cref="IResult"/> back to MVC. <c>JsonRpcResponseResult</c>
    /// ships with A2A.AspNetCore and already writes the envelope correctly, so the
    /// controller reuses it rather than reimplementing the wire format.
    /// </summary>
    private static IActionResult JsonRpc(JsonRpcResponse response) =>
        new HttpResultActionResult(new JsonRpcResponseResult(response));

    private static string Preview(Message? message)
    {
        string text = message?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        return text.Length <= 60 ? text : text[..60] + "…";
    }
}
