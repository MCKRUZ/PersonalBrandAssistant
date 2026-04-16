# Section 01: Project Setup

## Overview

This is the foundation section — no C# logic, just build infrastructure changes. Everything else in the plan depends on this compiling cleanly. The work is: add four NuGet packages to `Infrastructure.csproj`, add a `<Content>` item for the skills directory, and scaffold three new configuration sections in `appsettings.json` and `appsettings.Development.json`.

**Blocks:** All other sections depend on this being complete first.
**Dependencies:** None.

---

## Test First

No dedicated test file for this section. The test is a build verification.

```
dotnet build src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
```

Expected: exit code 0, zero errors, zero new warnings. This must pass before any subsequent section begins work.

---

## Files to Modify

### 1. `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj`

**Add NuGet package references** to the existing `<ItemGroup>` containing `PackageReference` entries:

```xml
<PackageReference Include="YamlDotNet" Version="16.3.0" />
<PackageReference Include="OpenTelemetry" Version="1.11.2" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.11.2" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.11.1" />
```

Notes:
- `Markdig` is already present at version `0.38.0` — do not add it again.
- `YamlDotNet` is the only missing parsing dependency.
- Pin all OTel packages to consistent versions. `1.11.x` is the stable line for .NET 10 compatibility — verify on NuGet before committing.

**Add a `<Content>` item** for the skills directory:

```xml
<Content Include="skills\**\*">
  <CopyToOutputDirectory>Always</CopyToOutputDirectory>
</Content>
```

Use `Always` (not `PreserveNewest`) to guarantee files are up-to-date on every build. The `skills/` directory is created in Section 07. The `<Content>` item matches zero files if absent — this is intentional.

### 2. `src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj`

Add the OTLP exporter package (needed for `Program.cs` wiring in Section 10):

```xml
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.11.2" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.11.2" />
```

`AddOtlpExporter()` requires the explicit package in the consuming project.

### 3. `src/PersonalBrandAssistant.Api/appsettings.json`

Add three new top-level sections after the existing `"Sidecar"` block or at the end before `"AllowedHosts"`:

```json
"Telemetry": {
  "ConsoleExporter": false,
  "OtlpEndpoint": null
},
"Skills": {
  "SkillsPath": ""
},
"ContextBudget": {
  "NudgeThreshold": 80000,
  "StopThreshold": 180000,
  "HardMaxTokens": 200000
},
```

`Skills:SkillsPath` empty string means "use default" (`AppContext.BaseDirectory/skills/` at runtime). `ContextBudgetOptions` interprets empty/null as the default path — do not put an actual path here.

Note (document in commit message, not inline JSON): "ContextBudget thresholds assume a 200k context window model. Adjust via ContextBudgetOptions if using a model with a different context window."

### 4. `src/PersonalBrandAssistant.Api/appsettings.Development.json`

If this file does not exist, create it. Add:

```json
{
  "Telemetry": {
    "ConsoleExporter": true
  }
}
```

This enables console span output in local development. The file is picked up automatically by `WebApplication.CreateBuilder` environment-based config loading.

---

## Verification

After all four file changes:

```bash
dotnet build src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj
dotnet build src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj
```

Both must exit 0 with zero new warnings.

---

## Known Limitation (Document in PR)

Single-file publish (`dotnet publish -p:PublishSingleFile=true`) is not supported in Phase 1. The `skills/**/*` `<Content>` item copies files to the output directory alongside the binary; single-file publish would require embedded resource handling. Out of scope for Phase 1.

---

## What This Section Does NOT Include

- No C# source files are created here.
- Do not create the `skills/` directory — that is Section 07.
- Do not wire `AddOpenTelemetry()` in `Program.cs` — that is Section 10.
- Do not create `SkillOptions.cs`, `ContextBudgetOptions.cs`, or any domain types — those are Sections 02 and later.

---

## As-Built Notes

**Files modified:**
- `src/PersonalBrandAssistant.Infrastructure/PersonalBrandAssistant.Infrastructure.csproj` — added YamlDotNet 16.3.0, OTel 1.11.2 packages, skills `<Content>` item
- `src/PersonalBrandAssistant.Api/PersonalBrandAssistant.Api.csproj` — added OTLP exporter + Extensions.Hosting with explanatory comment
- `src/PersonalBrandAssistant.Api/appsettings.json` — added Telemetry, Skills, ContextBudget sections
- `src/PersonalBrandAssistant.Api/appsettings.Development.json` — added `ConsoleExporter: true` (gitignored, not committed)

**Deviations from plan:**
- `appsettings.Development.json` is gitignored and was updated locally only (not committed). The plan assumed it might not exist; it already did with Logging config.
- Added XML comment to `OpenTelemetry.Extensions.Hosting` in Api.csproj to clarify intentional duplication (code reviewer flagged it as redundant — it's explicit because `AddOpenTelemetry()` is registered in the Api layer's `Program.cs`).

**Pre-existing build issue (not caused by this section):**
- `NU1903` on `System.Security.Cryptography.Xml` 10.0.5 — confirmed present before these changes via `git stash` test. Build was already failing.

**`OpenTelemetry.Instrumentation.AspNetCore` stays at 1.11.1** — latest available for that package; core OTel is 1.11.2.
