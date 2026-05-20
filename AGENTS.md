# AGENTS

## Project Scope
- Library: JsonPathPlus for JSONPath-like extraction over JSON streams.
- Target framework: net8.0; net9.0; net10.0; net11.0.
- Published as NuGet package: [JsonPathPlus](https://www.nuget.org/packages/JsonPathPlus)
- Online evaluator (sibling workspace): [jsonpath-plus-online-evaluator](https://dimq1.github.io/jsonpath-plus-online-evaluator/)

## Start Here
- Product and feature status: [README.md](README.md)
- Refactor workflow skill: [.github/skills/simple-solid-performance/SKILL.md](.github/skills/simple-solid-performance/SKILL.md)

## Build And Test
- Build library only:
  - `dotnet build JsonPathPlus.csproj -v minimal`
- Run focused extension tests:
  - `dotnet test tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj --filter FullyQualifiedName~StreamJsonExtractionExtensionsTests -v minimal`
- Run focused validator tests:
  - `dotnet test tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj --filter FullyQualifiedName~JsonPathValidatorTests -v minimal`
- Run all tests:
  - `dotnet test tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj -v minimal`

## Public API (all extension methods on Stream, JsonNode, string)
- `ExtractFirstJsonMatchAsync(this Stream|JsonNode?|string source, string? selectToken)` → `Task<JsonNode?>`
- `ExtractAllJsonMatchesAsync(this Stream|JsonNode?|string source, string? selectToken)` → `IAsyncEnumerable<JsonNode?>`
- `JsonPathValidator.IsValid(string? path)` → `bool`
- `JsonPathValidator.Validate(string? path)` → `JsonPathValidationResult` (record: `IsValid`, `Error`)

Passing `null` or `"$"` returns the entire document. Stream method uses streaming optimization for large root-level arrays.

## Internal Architecture
- [StreamJsonExtractionExtensions.cs](StreamJsonExtractionExtensions.cs) — public facade, delegates to core
- [JsonPathExtractionCore.cs](JsonPathExtractionCore.cs) — orchestrator; routing between streaming/full-parse
- [JsonPathParser.cs](JsonPathParser.cs) — tokenizer; manual span slicing, no regex
- [JsonPathMatcher.cs](JsonPathMatcher.cs) — tree traverser; delegates filters/computed to dedicated evaluators
- [JsonPathSegment.cs](JsonPathSegment.cs) — data model; 11 segment types in `JsonPathSegmentType` enum
- [JsonPathFilterEvaluator.cs](JsonPathFilterEvaluator.cs) — logical/comparison operators
- [JsonPathComputedExpressionEvaluator.cs](JsonPathComputedExpressionEvaluator.cs) — arithmetic expressions against array length
- [JsonPathStreamingMatcher.cs](JsonPathStreamingMatcher.cs) — streaming optimization (root arrays only)
- [JsonPathValidator.cs](JsonPathValidator.cs) — syntax validation without execution

All internal classes. Keep extension facade thin.

## Conventions For Changes
- Keep public behavior stable unless explicitly asked.
- Prefer small, focused refactors and preserve existing naming style.
- Keep extension facade thin; place parsing and matching logic in dedicated internal classes.
- Add or update targeted tests when changing path parsing or matching behavior.
- Test framework: xUnit 2.9.x (`[Fact]`, `[Theory]`/`[InlineData]`). Test classes are sealed.

## Known Pitfalls
- SDK-style projects include nested .cs files by default.
- Tests under tests/ must stay excluded from library compile (see [JsonPathPlus.csproj](JsonPathPlus.csproj)).
- README may lag behind renames; verify actual API names in code before updating callsites.
- Streaming optimization only activates for root-level arrays with wildcard/index/range/filter as first segment; falls back to full parse otherwise.
- Corporate NuGet feeds (Azure Artifacts) may cause 401 during restore; use `--source https://api.nuget.org/v3/index.json` or project-local `nuget.config`.

## High-Value Files
- Public entrypoints: [StreamJsonExtractionExtensions.cs](StreamJsonExtractionExtensions.cs)
- Path parser: [JsonPathParser.cs](JsonPathParser.cs)
- Path matcher: [JsonPathMatcher.cs](JsonPathMatcher.cs)
- Segment model: [JsonPathSegment.cs](JsonPathSegment.cs)
- Validator: [JsonPathValidator.cs](JsonPathValidator.cs)
- Regression tests: [tests/JsonPathPlus.Tests/StreamJsonExtractionExtensionsTests.cs](tests/JsonPathPlus.Tests/StreamJsonExtractionExtensionsTests.cs)

## Agent Behavior Guidance
- Link to existing docs instead of duplicating long explanations.
- Do not implement roadmap/planned features unless asked.
- Prefer targeted test runs first, then broader runs when needed.
