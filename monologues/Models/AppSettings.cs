namespace mystickymonologues.Models;

public class AppConfiguration
{
    public AppSettings AppSettings { get; set; } = new();
}

public class AppSettings
{
    public int MaxWindows { get; set; } = 5;
    public string NotesFolder { get; set; } = "";
    public bool IsSetupComplete { get; set; } = false;
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string AIProvider { get; set; } = "OpenAI";
    public string AIModelId { get; set; } = "whisper-1";

    /// <summary>Per-model API keys keyed by ModelId.</summary>
    public Dictionary<string, string> AIApiKeys { get; set; } = new();

    /// <summary>Legacy single key – kept only for one-time migration; do not use directly.</summary>
    public string AIApiKey { get; set; } = "";

    public string AIKeyFilePath { get; set; } = "";

    /// <summary>Enable local MCP server for Claude Desktop integration (default false).</summary>
    public bool MCPServerEnabled { get; set; } = false;

    /// <summary>Port the local MCP server listens on (default 3000).</summary>
    public int MCPServerPort { get; set; } = 3000;

    /// <summary>Run MCP server in separate process (default false = in-process).</summary>
    public bool MCPServerSeparateProcess { get; set; } = false;

    // Store positions for windows by index
    public List<WindowPosition> WindowPositions { get; set; } = new();
}

public class WindowPosition
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

/// <summary>
/// Represents a saved note entry for the notes browser.
/// </summary>
public class NoteEntry
{
    public string FilePath { get; set; } = "";
    public DateTime Date { get; set; }
    public string WindowId { get; set; } = "";
    /// <summary>First 3 non-empty lines of the note as a preview.</summary>
    public string Preview { get; set; } = "";
    public int TotalLines { get; set; }
}
