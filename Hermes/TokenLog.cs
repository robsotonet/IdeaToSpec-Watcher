using System.Globalization;

namespace Hermes;

/// <summary>
/// Appends one CSV row per LLM call:
/// timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd,wall_seconds,ollama_seconds
/// where est_frontier_usd = in/1e6*InputRate + out/1e6*OutputRate, wall_seconds is
/// total elapsed time, and ollama_seconds is the model's inference time (empty if
/// Ollama didn't report it).
/// This is the data that answers "should I pay for a frontier model?" later.
/// Caveat: ±10-15% vs. a real frontier bill (tokenizer differences).
/// </summary>
public sealed class TokenLog
{
    private const string Header = "timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd,wall_seconds,ollama_seconds";

    // The pre-duration-logging header. A log written before wall/ollama columns existed
    // starts with this line; it is upgraded to Header on the next append so new rows align.
    private const string LegacyHeader = "timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd";

    private readonly HermesConfig _config;

    public TokenLog(HermesConfig config) => _config = config;

    /// <summary>Estimated cost of this call had it run on a frontier model, in USD.</summary>
    public double EstimateFrontierUsd(GenerationResult result) =>
        result.InputTokens / 1_000_000.0 * _config.FrontierInputRatePerM +
        result.OutputTokens / 1_000_000.0 * _config.FrontierOutputRatePerM;

    /// <summary>Append a usage row for one generation call, creating the file + header if needed.</summary>
    public void Append(string intentFile, GenerationResult result)
    {
        var path = _config.TokenLog;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Bring a pre-existing log's header up to date before appending, so the new
        // wall/ollama columns line up with the header rather than drifting past it.
        UpgradeLegacyHeader(path);

        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
        var estUsd = EstimateFrontierUsd(result).ToString("F4", CultureInfo.InvariantCulture);
        var wallSeconds = result.WallSeconds.ToString("F1", CultureInfo.InvariantCulture);
        // Empty field when Ollama didn't report total_duration.
        var ollamaSeconds = result.OllamaSeconds is double os
            ? os.ToString("F1", CultureInfo.InvariantCulture)
            : "";
        var row = string.Join(",",
            timestamp,
            Csv(intentFile),
            Csv(_config.Model),
            result.InputTokens.ToString(CultureInfo.InvariantCulture),
            result.OutputTokens.ToString(CultureInfo.InvariantCulture),
            estUsd,
            wallSeconds,
            ollamaSeconds);

        // Open (or create) the file and decide on the header from the actual stream
        // length, so an empty file doesn't end up headerless.
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        stream.Seek(0, SeekOrigin.End);
        using var writer = new StreamWriter(stream);
        if (stream.Length == 0)
            writer.WriteLine(Header);
        writer.WriteLine(row);
    }

    /// <summary>
    /// If <paramref name="path"/> is an existing log whose first line is the pre-duration
    /// LegacyHeader, rewrite just that line to the current Header. Data rows are left as-is
    /// (older rows keep their six fields; the two new columns read as empty for them). No-op
    /// for a missing/empty file, an already-current header, or any unrecognized first line.
    /// The rewrite streams through a temp file (preserving the source encoding) and finishes
    /// with an atomic rename-overwrite, so a crash can never leave the log missing or partial.
    /// </summary>
    private static void UpgradeLegacyHeader(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            return;

        var tempPath = path + ".tmp";
        try
        {
            using (var reader = new StreamReader(path))
            {
                var firstLine = reader.ReadLine();
                // Only touch a header we recognize as the old one; leave current/unknown alone.
                if (firstLine != LegacyHeader)
                    return;

                // Stream the remaining rows through untouched rather than buffering the
                // whole (potentially large) log, and preserve the source encoding/BOM.
                using var writer = new StreamWriter(tempPath, append: false, reader.CurrentEncoding);
                writer.WriteLine(Header);
                string? line;
                while ((line = reader.ReadLine()) != null)
                    writer.WriteLine(line);
            }

            // Atomic rename-overwrite (rename(2) on Unix, MoveFileEx-replace on Windows):
            // no window where the log is missing, unlike a delete-then-move.
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // Clean up the temp file if we bailed early (unrecognized header) or threw
            // before the move completed.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    /// <summary>
    /// CSV field quoting for values that may contain commas, quotes, or newlines, plus a guard
    /// against spreadsheet formula injection: a field starting with = + - @ (or a tab) is prefixed
    /// with an apostrophe so Excel/Sheets treats it as text rather than an executable formula.
    /// </summary>
    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t".IndexOf(value[0]) >= 0)
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
