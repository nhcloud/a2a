namespace A2A.Demo.Client.Demos;

/// <summary>One runnable beat of the talk.</summary>
public interface IDemoScenario
{
    /// <summary>Menu key, also accepted as a command-line argument.</summary>
    string Key { get; }

    string Title { get; }

    /// <summary>The point this demo makes, shown under the title.</summary>
    string Summary { get; }

    Task RunAsync(CancellationToken cancellationToken);
}
