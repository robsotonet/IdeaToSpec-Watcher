namespace Hermes;

/// <summary>
/// Strongly-typed view of appsettings.json. The three primary, user-editable
/// settings are <see cref="SourceDir"/>, <see cref="DestinationDir"/> and
/// <see cref="LlmServerUrl"/>; everything else has a sensible default.
/// </summary>
public sealed class HermesConfig
{
    // --- Primary settings (the knobs a user is expected to change) ---

    /// <summary>Source folder: intent *.md files are read from here.</summary>
    public string SourceDir { get; set; } = "";

    /// <summary>Destination folder: generated spec *.md files are written here.</summary>
    public string DestinationDir { get; set; } = "";

    /// <summary>LLM server address (Ollama base URL). Requests go to {LlmServerUrl}/api/generate.</summary>
    public string LlmServerUrl { get; set; } = "";

    // --- Secondary settings (defaults are fine for v0.1) ---

    public string Model { get; set; } = "gpt-oss:20b";

    /// <summary>Where processed intents are moved. Defaults to {SourceDir}/_processed.</summary>
    public string? ProcessedDir { get; set; }

    public string TokenLog { get; set; } = "token-usage.csv";
    public string SystemPromptPath { get; set; } = "Prompts/systemPrompt.md";
    public int RequestTimeoutSeconds { get; set; } = 180;
    public double FrontierInputRatePerM { get; set; } = 3.00;
    public double FrontierOutputRatePerM { get; set; } = 15.00;

    /// <summary>Effective processed dir, falling back to {SourceDir}/_processed.</summary>
    public string EffectiveProcessedDir =>
        string.IsNullOrWhiteSpace(ProcessedDir)
            ? Path.Combine(SourceDir, "_processed")
            : ProcessedDir;

    /// <summary>
    /// Validate the three primary settings up front. Creates the source,
    /// destination and processed folders if missing; verifies the LLM URL parses.
    /// Throws <see cref="InvalidOperationException"/> with a clear message on failure.
    /// </summary>
    public void ValidateAndPrepare()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SourceDir))
            errors.Add("SourceDir (source folder) is not set.");
        if (string.IsNullOrWhiteSpace(DestinationDir))
            errors.Add("DestinationDir (destination folder) is not set.");
        if (string.IsNullOrWhiteSpace(LlmServerUrl))
            errors.Add("LlmServerUrl (LLM server address) is not set.");
        else if (!Uri.TryCreate(LlmServerUrl, UriKind.Absolute, out _))
            errors.Add($"LlmServerUrl is not a valid absolute URL: '{LlmServerUrl}'.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid Hermes configuration:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(e => "  - " + e)));

        // Ensure the folders exist so downstream steps don't have to.
        Directory.CreateDirectory(SourceDir);
        Directory.CreateDirectory(DestinationDir);
        Directory.CreateDirectory(EffectiveProcessedDir);
    }
}
