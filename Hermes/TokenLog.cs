using System.Globalization;

namespace Hermes;

/// <summary>
/// Appends one CSV row per LLM call:
/// timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd
/// where est_frontier_usd = in/1e6*InputRate + out/1e6*OutputRate.
/// This is the data that answers "should I pay for a frontier model?" later.
/// Caveat: ±10-15% vs. a real frontier bill (tokenizer differences).
/// </summary>
public sealed class TokenLog
{
    private const string Header = "timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd";

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

        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture);
        var estUsd = EstimateFrontierUsd(result).ToString("F4", CultureInfo.InvariantCulture);
        var row = string.Join(",",
            timestamp,
            Csv(intentFile),
            Csv(_config.Model),
            result.InputTokens.ToString(CultureInfo.InvariantCulture),
            result.OutputTokens.ToString(CultureInfo.InvariantCulture),
            estUsd);

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
