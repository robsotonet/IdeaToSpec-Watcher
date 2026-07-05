---
aliases: [Hermes Build Plan, Dockmaster Build]
tags: [homelab, hermes, dotnet, build-plan, goalbindery, agentic]
created: 2026-07-04
status: ready-to-build
---

# Hermes Build Plan — Phase 1 (.NET Console App)

The executable spec for building **Hermes v0.1**, the thinking-captain app that runs on devRyzen (role: dockmaster). Pick this up cold and build. See [[Hermes Orchestrator]] for the design rationale, [[UbuntuAI01 Engine Room]] for the model host.

**Build with Claude Code / Cowork on the Mac** (right tool for .NET), not hand-copied from chat.

## What it does (one sentence)
On a schedule, read new intent files from `dockmaster-io/intent/`, call the local LLM on UbuntuAI01 to turn each into a structured spec (user stories + acceptance criteria + `[toil]`/`[decision]`-tagged tasks), write the spec to `dockmaster-io/specs/`, and log token usage.

## Provisioning already done (do NOT redo)
- devRyzen on Tailscale; reaches `http://ubuntuai01:11434` (verified).
- .NET 9 SDK installed.
- Syncthing headless service, tailnet-only; `dockmaster-io` synced Mac↔QNAP↔devRyzen; vault Receive-Only + `.stignore`.
- Folders on devRyzen: `C:\DevWork\dockmaster-io\intent\`, `...\specs\`.

## Project structure
```
Hermes/                      (solution / repo — its OWN git repo, not inside the vault)
  Hermes.csproj              (.NET 9 console app)
  appsettings.json           (config: paths, model, Ollama URL, rates)
  Program.cs                 (entry point + loop)
  OllamaClient.cs            (HTTP calls to Ollama, returns text + token counts)
  IntentReader.cs            (find new *.md in intent/, mark processed)
  SpecWriter.cs              (write spec markdown to specs/)
  TokenLog.cs                (append usage to a CSV)
  Prompts/systemPrompt.md    (the instruction that shapes output)
```
Keep it small. No framework (no CrewAI/AutoGen/Semantic Kernel needed for v0.1). Plain `HttpClient`.

## Config (appsettings.json)
> **Note (as implemented):** the setting names below were renamed during the build for clarity — `OllamaUrl`→`LlmServerUrl`, `IntentDir`→`SourceDir`, `SpecDir`→`DestinationDir`. See `Hermes/README.md` and `Hermes/appsettings.json` for the authoritative config. The snippet below is the original design.
```json
{
  "OllamaUrl": "http://ubuntuai01:11434",
  "Model": "gpt-oss:20b",
  "IntentDir": "C:\\DevWork\\dockmaster-io\\intent",
  "SpecDir": "C:\\DevWork\\dockmaster-io\\specs",
  "ProcessedDir": "C:\\DevWork\\dockmaster-io\\intent\\_processed",
  "TokenLog": "C:\\DevWork\\dockmaster-io\\token-usage.csv",
  "FrontierInputRatePerM": 3.00,
  "FrontierOutputRatePerM": 15.00
}
```
Model choice: start with `gpt-oss:20b` (strong reasoning, fits VRAM). Benchmark against `devstral:24b` and `llama3.1:8b` on real intent; keep whichever writes the best user stories. Swappable via config.

## The Ollama call (OllamaClient.cs)
POST to `{OllamaUrl}/api/generate`:
```json
{ "model": "gpt-oss:20b", "prompt": "<system + intent>", "stream": false }
```
Response JSON fields to capture:
- `response` — the generated text (the spec)
- `prompt_eval_count` — INPUT tokens  → log
- `eval_count` — OUTPUT tokens → log
Set a generous `HttpClient.Timeout` (local generation of a full spec can take 30-90s+).

## The system prompt (Prompts/systemPrompt.md) — the heart of it
Instruct the model to output, for a given rough intent about GoalBindery:
1. A short restatement of the goal.
2. One or more **user stories** in "As a <role>, I want <capability>, so that <benefit>" form.
3. **Acceptance criteria** per story (Given/When/Then or checklist).
4. A **task breakdown**, each task tagged:
   - `[toil]` — below Rob's decision threshold; safe to delegate to a coding agent later (boilerplate, connection strings, logging, CRUD).
   - `[decision]` — at Rob's threshold; needs Rob + Claude (architecture, tradeoffs, external-dependency choices, security).
5. Any **open questions / assumptions** it had to make.
Tell it: be concrete, match a .NET/React/Supabase stack, prefer small vertical slices, and flag anything it's unsure about rather than inventing.

## Spec output format (SpecWriter.cs)
Write one Markdown file per intent to `specs/`, named `spec-<intent-filename>-<yyyymmdd-hhmm>.md`, with YAML frontmatter (`source_intent`, `model`, `generated`, `in_tokens`, `out_tokens`) then the model's output. Rob reviews these in Obsidian/Cowork on the Mac (they sync back automatically).

## Token log format (TokenLog.cs)
Append a CSV row per call: `timestamp,intent_file,model,in_tokens,out_tokens,est_frontier_usd`
where `est_frontier_usd = in/1e6*InputRate + out/1e6*OutputRate`. This is the data that answers "should I pay for a frontier model?" later (see [[Idea Bucket List]]). Caveat: ±10-15% vs. real frontier bill (tokenizer differences).

## Intent handling (IntentReader.cs)
- Scan `intent/` for `*.md` not yet processed.
- After processing, MOVE the intent file to `intent/_processed/` (so it isn't reprocessed). Add `_processed` to the folder's ignore or just let it sync — decide at build.
- Idempotent: safe to run repeatedly; only new files get processed.

## Scheduling
Windows Task Scheduler, same headless pattern as Syncthing (run as `dev.rob`, whether logged on or not). Two triggers worth having: nightly (e.g. 2am) + on-demand (run manually to test). Start with on-demand only until it's proven, then add the schedule.

## Phase 1 EXIT CRITERIA (don't build Phase 2 until all true)
- [ ] Produces user stories Rob accepts with minor edits, not rewrites.
- [ ] Tags `[toil]` vs `[decision]` correctly most of the time.
- [ ] Runs unattended on schedule without breaking.
- [ ] Actually saves Rob time directing GoalBindery work.
- [ ] Token log produces a credible frontier cost estimate.

## Build order (session tasks)
1. `dotnet new console -n Hermes`, add `appsettings.json` + config binding.
2. `OllamaClient` — get a hardcoded prompt returning text + token counts from ubuntuai01. **Prove the pipe first.**
3. `systemPrompt.md` — draft, iterate on one real GoalBindery intent until the spec is good.
4. `IntentReader` + `SpecWriter` — wire the folder in/out loop.
5. `TokenLog` — add usage logging.
6. Test end-to-end: drop an intent on the Mac → run Hermes on devRyzen → review the spec back on the Mac.
7. Once good: add the nightly Task Scheduler trigger.

## Explicitly OUT of scope for Phase 1
- Discord / war room (Phase 2+, when there's a second captain to coordinate).
- The gaming-PC coding captain (Phase 2).
- Any code-writing/merging by Hermes (it writes specs, never code).
- Frontier API (local Ollama only until token data justifies the upgrade).

## Related
- [[Hermes Orchestrator]] — design & rationale
- [[UbuntuAI01 Engine Room]] — the model host
- [[Idea Bucket List]] — parked items (Discord, Mission Control, frontier upgrade)
- Future: [[GoalBindery]]
