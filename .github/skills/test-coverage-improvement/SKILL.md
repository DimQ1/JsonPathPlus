---
name: test-coverage-improvement
description: >
  Analyze code coverage metrics and add new unit tests to maximize line and branch coverage.
  Use when asked to improve test coverage, add missing tests, boost coverage metrics,
  or analyze what's not covered.
argument-hint: 'Target file, class, or project to improve coverage for'
user-invocable: true
---

# Test Coverage Improvement

## When to Use
Use this skill when asked to:
- analyze unit test coverage and identify gaps
- add tests to cover uncovered lines and branches
- improve code coverage metrics for a specific file or project
- create comprehensive test suites for existing code
- review existing tests and suggest improvements
- add edge-case and boundary tests to catch regressions

Trigger phrases:
- improve test coverage
- add more test cases
- analyze coverage gaps
- boost coverage metrics
- what's not covered?
- add missing tests
- cover edge cases

## Outcome
Produce a test suite with:
- measurably higher line and branch coverage
- targeted tests for previously uncovered code paths
- edge case, null, boundary, and error-path coverage
- tests that follow existing project conventions (framework, naming, patterns)

---

## Workflow

### Step 1 — Run Tests with Coverage
```bash
dotnet test tests/<TestProject>/<TestProject>.csproj --collect:"XPlat Code Coverage" --results-directory artifacts/coverage
```

If `XPlat Code Coverage` is unavailable, add coverlet:
```bash
dotnet add tests/<TestProject>/<TestProject>.csproj package coverlet.collector
```

### Step 2 — Parse Coverage Report
Extract per-file metrics from the Cobertura XML:
```powershell
$xml = [xml](Get-Content "artifacts/coverage/<guid>/coverage.cobertura.xml" -Raw)
$xml.coverage.packages.package.classes.class | ForEach-Object {
  $lines = $_.lines.line
  $covered = ($lines | Where-Object { $_.hits -gt 0 } | Measure-Object).Count
  $total = $lines.Count
  [PSCustomObject]@{ File = ($_.filename -replace '.*\\',''); Coverage = "{0:P1}" -f ($covered/$total) }
} | Sort-Object Coverage
```

### Step 3 — Find Uncovered Lines
Identify specific uncovered lines to target:
```powershell
$xml.coverage.packages.package.classes.class | ForEach-Object {
  $lines = $_.lines.line
  $uncovered = $lines | Where-Object { $_.hits -eq "0" }
  if ($uncovered) {
    [PSCustomObject]@{ File = ($_.filename -replace '.*\\',''); UncoveredLines = ($uncovered.number -join ', ') }
  }
}
```

### Step 4 — Analyze Coverage Gaps
For each low-coverage file, map uncovered lines to code constructs:
- **Branches not taken**: comparison operators (==, !=, <=, >=), logical operators (||)
- **Error paths**: null inputs, invalid data, out-of-range values
- **Fallback paths**: `JsonElement` fallback, `ReadOnlyMemory<byte>` paths, edge-of-stream
- **Internal type tests**: record structs, enums, sealed classes
- **Path-tracking methods**: `CollectXxxMatchesWithPaths` variants for all segment types
- **Extension overloads**: `string`, `JsonNode`, `Stream` overloads with options

### Step 5 — Write Targeted Tests
Follow these patterns when writing new tests:

#### Pattern 1: Test public API (preferred for internal types)
If the uncovered code is in an `internal` class, test through the public API:
```csharp
// Instead of testing JsonPathFilterEvaluator directly,
// test through ExtractFirstJsonMatchAsync with appropriate filter expressions
var result = await stream.ExtractFirstJsonMatchAsync("$.items[?(@.price == 5)].name");
```

#### Pattern 2: Test all comparison operators
For filter evaluators, ensure each operator is exercised:
```csharp
"$.items[?(@.x == 5)]"   // equals
"$.items[?(@.x != 5)]"   // not equals
"$.items[?(@.x < 5)]"     // less than
"$.items[?(@.x <= 5)]"    // less than or equal
"$.items[?(@.x > 5)]"     // greater than
"$.items[?(@.x >= 5)]"    // greater than or equal
```

#### Pattern 3: Test logical operators
```csharp
"$.items[?(@.a && @.b)]"  // AND
"$.items[?(@.a || @.b)]"  // OR
"$.items[?(!@.a)]"        // NOT
```

#### Pattern 4: Test all segment types for path tracking
For `ExtractAllJsonMatchesWithPathsAsync`, test each segment type:
- Property, ArrayIndex, ArrayRange, ArrayUnion, PropertyUnion
- Filter, ComputedIndex, Wildcard, RecursiveDescent
- FieldProjection, FieldExclusion, FieldExistence, FieldCount, NestedQuery

#### Pattern 5: Test extension method overloads
```csharp
// Stream overload
await stream.ExtractFirstJsonMatchAsync("$.x")
// Stream with options
await stream.ExtractAllJsonMatchesAsync("$.x", options)
// String overload
await json.ExtractFirstJsonMatchAsync("$.x")
// JsonNode overload
await node.ExtractFirstJsonMatchAsync("$.x")
// With paths
await stream.ExtractAllJsonMatchesWithPathsAsync("$.x")
```

#### Pattern 6: Test error and edge cases
```csharp
// Null stream
await Assert.ThrowsAsync<ArgumentNullException>(() => ((Stream)null!).ExtractFirstJsonMatchAsync("$.x"))
// Null node
await ((JsonNode?)null).ExtractFirstJsonMatchAsync("$.x") // should not throw
// FullParseMaxBytes exceeded
await Assert.ThrowsAsync<InvalidOperationException>(() => CollectAsync(stream.ExtractAllJsonMatchesAsync("$..x", options)))
// Not-found path
var result = await stream.ExtractFirstJsonMatchAsync("$.does.not.exist")
Assert.Null(result)
```

### Step 6 — Run and Verify
1. Build: `dotnet build tests/<TestProject>/<TestProject>.csproj -v minimal`
2. Run: `dotnet test tests/<TestProject>/<TestProject>.csproj`
3. Re-measure coverage and compare metrics
4. Ensure no regressions (all existing tests still pass)

---

## Test File Organization
- **One test class per concern**: `StreamJsonFilterEdgeCaseTests`, `StreamJsonWithPathsTests`, etc.
- **Follow existing naming**: Match the project's test naming conventions
- **Use const strings for test data**: Keep JSON test data at the top of each test class
- **Group tests with section comments**: `// ── OR operator ──`
- **Use helper methods**: `CreateStream()`, `CollectAsync()`, `AssertNodeEquals()`

## Test Naming Convention
```
MethodName_Scenario_ExpectedBehavior
```
Example: `ExtractAll_WithFilterOrOperator_ReturnsMatchingElements`

---

## Coverage Targets
Aim for these minimums before considering a file "well-covered":
| Metric | Target |
|--------|--------|
| Line coverage | ≥ 90% |
| Branch coverage | ≥ 80% |
| Public API coverage | 100% |

Files with < 70% line coverage should be prioritized for new tests.

---

## Common Coverage Gaps in C# JSON/Path Libraries

| Gap | How to cover |
|-----|-------------|
| `false` branch of `if` statements | Provide test data that triggers each branch |
| `switch` cases not hit | Add test data for each case |
| `catch` blocks | Force exceptions with invalid input |
| Null propagation | Test with null at each level |
| `default` case in switch | Test with unexpected values |
| Record struct equality | Test value equality for record types |
| Iterator methods (`yield`) | Test empty, single, and multi-element collections |
| Async enumerables | Test with async iteration patterns |
| ReadOnlySpan paths | Test with empty, short, and long inputs |
| Number parsing fallbacks | Use `decimal.TryParse`, `double.TryParse`, `int.TryParse` |

## Related Skills
- **simple-solid-performance** — Refactor code before adding tests for improved structure
- **dotnet-best-practices** — Ensure tests follow .NET testing best practices
