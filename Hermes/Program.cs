using System.Globalization;
using Hermes;
using Microsoft.Extensions.Configuration;

// Environment selection: set DOTNET_ENVIRONMENT=Development to load the
// local Mac test overrides (appsettings.Development.json).
var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables(prefix: "HERMES_")
    .Build();

var config = configuration.Get<HermesConfig>() ?? new HermesConfig();

try
{
    config.ValidateAndPrepare();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

Console.WriteLine($"Hermes v0.1  (environment: {environment})");
Console.WriteLine($"  Intent dir (source)     : {config.IntentDir}");
Console.WriteLine($"  Spec dir (destination)  : {config.SpecDir}");
Console.WriteLine($"  Ollama URL (LLM server) : {config.OllamaUrl}");
Console.WriteLine($"  Model              : {config.Model}");
Console.WriteLine();

// --- Temporary: prove the Ollama pipe (build step 2) ---
// Usage: dotnet run -- test-ollama ["your prompt here"]
if (args.Length > 0 && args[0] == "test-ollama")
{
    var prompt = args.Length > 1
        ? args[1]
        : "In one short sentence, what is a build specification?";

    Console.WriteLine($"Sending prompt to {config.Model} @ {config.OllamaUrl} ...");
    Console.WriteLine($"  prompt: {prompt}");
    Console.WriteLine();

    using var ollama = new OllamaClient(config);
    var started = Environment.TickCount64;
    try
    {
        var result = await ollama.GenerateAsync(prompt);
        var elapsed = (Environment.TickCount64 - started) / 1000.0;

        Console.WriteLine("--- response ---");
        Console.WriteLine(result.Response.Trim());
        Console.WriteLine("--- tokens ---");
        Console.WriteLine($"  input (prompt_eval_count) : {result.InputTokens}");
        Console.WriteLine($"  output (eval_count)       : {result.OutputTokens}");
        Console.WriteLine($"  elapsed                   : {elapsed:F1}s");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Ollama call failed: {ex.Message}");
        return 1;
    }
}

// --- Temporary: generate a full spec from system prompt + one intent (build step 3) ---
// Usage: dotnet run -- test-spec <path-to-intent.md>
if (args.Length > 0 && args[0] == "test-spec")
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: test-spec <path-to-intent.md>");
        return 1;
    }

    var systemPromptFile = Path.Combine(AppContext.BaseDirectory, config.SystemPromptPath);
    var systemPrompt = await File.ReadAllTextAsync(systemPromptFile);
    var intent = await File.ReadAllTextAsync(args[1]);
    var fullPrompt = $"{systemPrompt}\n\n--- INTENT ---\n{intent}\n\n--- SPECIFICATION ---\n";

    Console.WriteLine($"Generating spec for '{Path.GetFileName(args[1])}' with {config.Model} ...");
    Console.WriteLine();

    using var ollama = new OllamaClient(config);
    var started = Environment.TickCount64;
    try
    {
        var result = await ollama.GenerateAsync(fullPrompt);
        var elapsed = (Environment.TickCount64 - started) / 1000.0;

        Console.WriteLine(result.Response.Trim());
        Console.WriteLine();
        Console.WriteLine($"--- in:{result.InputTokens} out:{result.OutputTokens} tokens, {elapsed:F1}s ---");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Spec generation failed: {ex.Message}");
        return 1;
    }
}

// --- Main pipeline: intent folder -> spec folder ---
{
    var reader = new IntentReader(config);
    var writer = new SpecWriter(config);
    var tokenLog = new TokenLog(config);
    using var ollama = new OllamaClient(config);

    var systemPromptFile = Path.Combine(AppContext.BaseDirectory, config.SystemPromptPath);
    var systemPrompt = await File.ReadAllTextAsync(systemPromptFile);

    var intents = reader.FindNew();
    if (intents.Count == 0)
    {
        Console.WriteLine("No new intents to process.");
        return 0;
    }

    Console.WriteLine($"Processing {intents.Count} intent(s)...");
    Console.WriteLine();

    var processed = 0;
    var failed = 0;
    foreach (var intent in intents)
    {
        Console.Write($"  {intent.Name} ... ");
        try
        {
            // Wall-clock: elapsed from processing start (intent read + Ollama call),
            // covering network, HTTP and IO overhead. Stopped just before the write
            // so the same figure can be embedded in the spec being written (the
            // write itself is therefore excluded).
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var intentText = await File.ReadAllTextAsync(intent.FullName);
            var fullPrompt = $"{systemPrompt}\n\n--- INTENT ---\n{intentText}\n\n--- SPECIFICATION ---\n";

            var result = await ollama.GenerateAsync(fullPrompt);

            stopwatch.Stop();
            result = result with { WallSeconds = stopwatch.Elapsed.TotalSeconds };

            var specPath = writer.Write(intent.Name, result);

            // Token logging is best-effort: a logging failure must not leave the
            // intent unprocessed (which would reprocess it and produce a duplicate spec).
            try
            {
                tokenLog.Append(intent.Name, result);
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine($"(warning: token log append failed: {logEx.Message})");
            }

            reader.MarkProcessed(intent);

            // Format numerics with InvariantCulture so the console line matches the
            // CSV/YAML output (which also use invariant) regardless of machine locale.
            var inTokens = result.InputTokens.ToString(CultureInfo.InvariantCulture);
            var outTokens = result.OutputTokens.ToString(CultureInfo.InvariantCulture);
            var estUsd = tokenLog.EstimateFrontierUsd(result).ToString("F4", CultureInfo.InvariantCulture);
            var wallSecs = result.WallSeconds.ToString("F1", CultureInfo.InvariantCulture);
            var ollamaSecs = result.OllamaSeconds is double os
                ? os.ToString("F1", CultureInfo.InvariantCulture) + "s"
                : "n/a";
            Console.WriteLine($"ok -> {Path.GetFileName(specPath)}  (in:{inTokens} out:{outTokens}, wall:{wallSecs}s ollama:{ollamaSecs}, ~${estUsd} frontier)");
            processed++;
        }
        catch (Exception ex)
        {
            // Leave the intent in place so it is retried on the next run.
            Console.WriteLine($"FAILED: {ex.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"Done. {processed} spec(s) written, {failed} failed.");
    return failed > 0 ? 1 : 0;
}
