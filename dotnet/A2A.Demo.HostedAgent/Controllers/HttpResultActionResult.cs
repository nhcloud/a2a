using Microsoft.AspNetCore.Mvc;

namespace A2A.Demo.HostedAgent.Controllers;

/// <summary>
/// Adapts a minimal-API <see cref="IResult"/> to MVC's <see cref="IActionResult"/>.
/// </summary>
/// <remarks>
/// A2A.AspNetCore ships <c>JsonRpcResponseResult</c> and <c>JsonRpcStreamedResult</c>,
/// which already write the JSON-RPC envelope and the SSE framing correctly. They are
/// <see cref="IResult"/> because the package was written for minimal APIs; this three-line
/// adapter is the whole cost of using them from a controller, and it beats reimplementing
/// the wire format.
/// </remarks>
internal sealed class HttpResultActionResult : IActionResult
{
    private readonly IResult _result;

    public HttpResultActionResult(IResult result) => _result = result;

    public Task ExecuteResultAsync(ActionContext context) => _result.ExecuteAsync(context.HttpContext);
}
