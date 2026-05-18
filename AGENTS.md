# AGENTS

## Project Scope
- Library: JsonPathPlus for JSONPath-like extraction over JSON streams.
- Target framework: net8.0.
- Main API surface is in [StreamJsonExtractionExtensions.cs](StreamJsonExtractionExtensions.cs).

## Start Here
- Product and feature status: [README.md](README.md)
- Refactor workflow skill: [.github/skills/simple-solid-performance/SKILL.md](.github/skills/simple-solid-performance/SKILL.md)

## Build And Test
- Build library only:
  - dotnet build JsonPathPlus.csproj -v minimal
- Run focused extension tests:
  - dotnet test tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj --filter FullyQualifiedName~StreamJsonExtractionExtensionsTests -v minimal
- Run all tests in test project:
  - dotnet test tests/JsonPathPlus.Tests/JsonPathPlus.Tests.csproj -v minimal

## Conventions For Changes
- Keep public behavior stable unless explicitly asked.
- Prefer small, focused refactors and preserve existing naming style.
- Keep extension facade thin; place parsing and matching logic in dedicated internal classes.
- Add or update targeted tests when changing path parsing or matching behavior.

## Known Pitfalls
- SDK-style projects include nested .cs files by default.
- Tests under tests/ must stay excluded from library compile (see [JsonPathPlus.csproj](JsonPathPlus.csproj)).
- README may lag behind renames; verify actual API names in code before updating callsites.

## High-Value Files
- Public entrypoints: [StreamJsonExtractionExtensions.cs](StreamJsonExtractionExtensions.cs)
- Path parser: [JsonPathParser.cs](JsonPathParser.cs)
- Path matcher: [JsonPathMatcher.cs](JsonPathMatcher.cs)
- Segment model: [JsonPathSegment.cs](JsonPathSegment.cs)
- Regression tests: [tests/JsonPathPlus.Tests/StreamJsonExtractionExtensionsTests.cs](tests/JsonPathPlus.Tests/StreamJsonExtractionExtensionsTests.cs)

## Agent Behavior Guidance
- Link to existing docs instead of duplicating long explanations.
- Do not implement roadmap/planned features unless asked.
- Prefer targeted test runs first, then broader runs when needed.
