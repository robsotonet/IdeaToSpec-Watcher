using System.Globalization;

namespace Hermes;

/// <summary>
/// Writes one spec markdown file per intent to the destination folder, named
/// spec-&lt;intent&gt;-&lt;yyyymmdd-hhmm&gt;.md with YAML frontmatter followed by the
/// model's output.
/// </summary>
public sealed class SpecWriter
{
    private readonly HermesConfig _config;

    public SpecWriter(HermesConfig config) => _config = config;

    /// <summary>
    /// Write a spec for <paramref name="intentFileName"/>. Returns the full path written.
    /// </summary>
    public string Write(string intentFileName, GenerationResult result)
    {
        Directory.CreateDirectory(_config.SpecDir);

        var now = DateTime.Now;
        var stamp = now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var intentStem = Sanitize(Path.GetFileNameWithoutExtension(intentFileName));

        // Keep the plan's minute-precision name, but de-duplicate if a spec for the
        // same intent already exists this minute rather than overwriting it.
        var path = Path.Combine(_config.SpecDir, $"spec-{intentStem}-{stamp}.md");
        var suffix = 2;
        while (File.Exists(path))
            path = Path.Combine(_config.SpecDir, $"spec-{intentStem}-{stamp}-{suffix++}.md");

        var generatedIso = now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);

        var content =
            "---\n" +
            $"source_intent: {Yaml(intentFileName)}\n" +
            $"model: {Yaml(_config.Model)}\n" +
            $"generated: {Yaml(generatedIso)}\n" +
            $"in_tokens: {result.InputTokens}\n" +
            $"out_tokens: {result.OutputTokens}\n" +
            $"wall_seconds: {result.WallSeconds.ToString("F1", CultureInfo.InvariantCulture)}\n" +
            // Empty scalar (null in YAML) when Ollama didn't report total_duration.
            $"ollama_seconds: {(result.OllamaSeconds is double os ? os.ToString("F1", CultureInfo.InvariantCulture) : "")}\n" +
            "---\n\n" +
            result.Response.TrimEnd() + "\n";

        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Replace any characters that are invalid in a filename with '_'.</summary>
    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    /// <summary>Quote a YAML string scalar so ':' '#' '\' etc. can't break parsing.</summary>
    private static string Yaml(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
