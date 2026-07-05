namespace Hermes;

/// <summary>
/// Finds new intent *.md files in the source folder and moves them to _processed
/// once handled. Idempotent: only files not already under _processed are returned,
/// so running repeatedly never reprocesses the same intent.
/// </summary>
public sealed class IntentReader
{
    private readonly HermesConfig _config;

    public IntentReader(HermesConfig config) => _config = config;

    /// <summary>New intent files in the source folder (top level only, excludes _processed).</summary>
    public IReadOnlyList<FileInfo> FindNew()
    {
        var source = new DirectoryInfo(_config.SourceDir);
        if (!source.Exists)
            return Array.Empty<FileInfo>();

        // Top-level *.md only; _processed is a subfolder so it is naturally excluded.
        return source.GetFiles("*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Move a handled intent into _processed so it is not picked up again.</summary>
    public void MarkProcessed(FileInfo intent)
    {
        Directory.CreateDirectory(_config.EffectiveProcessedDir);
        var target = Path.Combine(_config.EffectiveProcessedDir, intent.Name);

        // If a same-named file was processed before, disambiguate rather than throw.
        if (File.Exists(target))
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var name = Path.GetFileNameWithoutExtension(intent.Name);
            var ext = intent.Extension;
            target = Path.Combine(_config.EffectiveProcessedDir, $"{name}-{stamp}{ext}");
        }

        intent.MoveTo(target);
    }
}
