# Hermes v0.1

A small .NET 9 console app that turns rough **intent** notes into structured **specs**
using a local LLM (Ollama). On each run it reads new `*.md` files from a source folder,
asks the model to produce user stories + acceptance criteria + `[toil]`/`[decision]`-tagged
tasks, writes the spec to a destination folder, and logs token usage (with an estimated
frontier-model cost) to a CSV.

It writes **specs only, never code**. See `../.doc/Hermes Build Plan.md` for the full spec.

## Configuration

Everything lives in `appsettings.json`. The three primary, user-editable settings:

| Setting      | Meaning                                                        |
|--------------|----------------------------------------------------------------|
| `IntentDir`  | **Source folder** — intent `*.md` files are read from here     |
| `SpecDir`    | **Destination folder** — generated spec `*.md` files go here   |
| `OllamaUrl`  | **LLM server address** — Ollama base URL (`/api/generate`)     |

Secondary settings: `Model`, `ProcessedDir` (defaults to `IntentDir/_processed`), `TokenLog`,
`SystemPromptPath`, `RequestTimeoutSeconds`, `FrontierInputRatePerM`, `FrontierOutputRatePerM`.

All three primary settings are validated on startup — missing folders are created, a malformed
URL fails fast. Any setting can also be overridden with a `HERMES_`-prefixed environment variable
(e.g. `HERMES_Model=devstral:24b`, `HERMES_OllamaUrl=http://host:11434`).

### Environments
- **Production** (default): uses `appsettings.json` — the Windows paths on devRyzen.
- **Development**: set `DOTNET_ENVIRONMENT=Development` to overlay `appsettings.Development.json`
  (local test folders under `./_localdata`). Used for building/testing on the Mac.

## Run locally (Mac, for development)

```bash
# process any intents in ./_localdata/intent
DOTNET_ENVIRONMENT=Development dotnet run

# prove the LLM pipe with a throwaway prompt
DOTNET_ENVIRONMENT=Development dotnet run -- test-ollama "your prompt"

# generate a spec from one intent to stdout (prompt-tuning aid)
DOTNET_ENVIRONMENT=Development dotnet run -- test-spec _localdata/intent/some-intent.md
```

`_localdata/` is git-ignored.

## Deploy on devRyzen (Windows)

Prerequisites (already provisioned): .NET 9 SDK, Tailscale reaching `http://ubuntuai01:11434`,
Syncthing keeping `dockmaster-io` in sync, and the `C:\DevWork\dockmaster-io\intent|specs` folders.

1. **Get the code** onto devRyzen (clone the repo, or pull this branch).
2. **Confirm config** — `appsettings.json` should point at the real Windows paths and model.
3. **Publish**:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
   ```
   Produces `publish\Hermes.exe` with `appsettings.json` and `Prompts\` alongside it.

### Step 6 — on-demand run (prove it end-to-end)
Drop an intent `*.md` into `intent\` (or let one sync from the Mac), then:
```powershell
scripts\run-hermes.cmd
```
Check `specs\` for the new `spec-*.md` (it syncs back to the Mac) and `token-usage.csv` for the row.
Prove this works a few times before scheduling.

### Step 7 — nightly schedule
Once on-demand is proven, register the nightly task (runs 2 AM as `dev.rob`, whether logged on or not):
```powershell
powershell -ExecutionPolicy Bypass -File scripts\register-scheduled-task.ps1
```
Test / inspect / remove:
```powershell
Start-ScheduledTask   -TaskName 'Hermes Nightly'
Get-ScheduledTaskInfo -TaskName 'Hermes Nightly'
Unregister-ScheduledTask -TaskName 'Hermes Nightly' -Confirm:$false
```
Edit `scripts\register-scheduled-task.ps1` if `dev.rob` is a domain account or you want a different time.

## Output formats

**Spec** (`spec-<intent>-<yyyymmdd-hhmm>.md`, with a `-2`, `-3`… suffix if a spec for the same
intent already exists for that minute) — quoted YAML frontmatter then the model's markdown:
```yaml
---
source_intent: "password-strength-check.md"
model: "gpt-oss:20b"
generated: "2026-07-04T20:03:32-04:00"
in_tokens: 618
out_tokens: 1457
---
# Specification: ...
```

**Token log** (`token-usage.csv`):
```csv
timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd
2026-07-04T20:06:32-04:00,csv-export.md,gpt-oss:20b,488,1180,0.0192
```
`est_frontier_usd = in/1e6*FrontierInputRatePerM + out/1e6*FrontierOutputRatePerM`.
Caveat: ±10-15% vs. a real frontier bill (tokenizer differences).

## How it works

`Program.cs` loads config → for each new intent in `IntentDir`, `OllamaClient` sends
`systemPrompt.md` + the intent to the model → `SpecWriter` writes the spec → `TokenLog` appends
usage → `IntentReader` moves the intent to `_processed`. Idempotent: reruns only pick up new files;
a failed intent is left in place to retry next run.

| File                    | Role                                              |
|-------------------------|---------------------------------------------------|
| `HermesConfig.cs`       | Strongly-typed config + startup validation        |
| `OllamaClient.cs`       | POST `/api/generate`, return text + token counts  |
| `IntentReader.cs`       | Find new intents, move to `_processed`            |
| `SpecWriter.cs`         | Write spec markdown + frontmatter                 |
| `TokenLog.cs`           | Append CSV usage row + cost estimate              |
| `Prompts/systemPrompt.md` | The instruction that shapes the output          |

## Swapping models
Benchmark alternatives on real intent by changing `Model` (config or `HERMES_Model=...`):
`gpt-oss:20b` (default), `devstral:24b`, `llama3.1:8b`. Keep whichever writes the best user stories.
