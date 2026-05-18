# JsonPathPlus

Lightweight JSONPath-like `Stream` extensions for `System.Text.Json`.

Extracts JSON subtrees from a `Stream` by path expression without loading the full document into memory. Internally uses `JsonNode.ParseAsync` for evaluation; a true token-streaming mode (no full parse) is on the roadmap.

## Installation

```
dotnet add package JsonPathPlus
```

## Quick start

```csharp
using JsonPathPlus;

// First match
JsonNode? node = await stream.ExtractFirstJsonMatchAsync("$.document_data.items[0].seal");

// All matches
await foreach (var item in stream.ExtractAllJsonMatchesAsync("$.items[*].id"))
{
    Console.WriteLine(item);
}
```

## NuGet publishing (GitHub only)

NuGet publishing is automated in GitHub Actions and does not require local package push commands.

### Workflows

- Build validation: [.github/workflows/build.yml](.github/workflows/build.yml)
- NuGet publish: [.github/workflows/publish-nuget.yml](.github/workflows/publish-nuget.yml)

### Required GitHub secret

Add this repository secret before publishing:

- NUGET_API_KEY: API key from nuget.org with push permission for JsonPathPlus

### Release branch format

Publishing is triggered when you push a branch with this exact pattern:

- release_1.0.0
- release_1.0.1

The version is taken from the branch name after release_.
Only semantic versions in major.minor.patch format are accepted.

### Publish flow

When a release branch is pushed, [.github/workflows/publish-nuget.yml](.github/workflows/publish-nuget.yml) performs:

1. Restore
2. Build (Release) with /p:Version from branch name
3. Test
4. Pack with /p:PackageVersion from branch name
5. Push package to nuget.org with --skip-duplicate

### Typical commands

```bash
git checkout -b release_1.0.0
git push -u origin release_1.0.0
```

### Notes

- Local nupkg/snupkg outputs are ignored by git.
- If a branch name is invalid (for example release_1.0), the publish workflow fails fast with a validation error.
- If the same package version already exists on nuget.org, publish step is non-failing because --skip-duplicate is enabled.

## Supported path syntax

| Syntax | Example | Meaning | Status |
|---|---|---|---|
| Root | `$` | Entire document | ✅ Implemented |
| Property | `$.name` | Named property | ✅ Implemented |
| Dot-chain | `$.a.b.c` | Nested properties | ✅ Implemented |
| Array index | `$.items[0]` | Single element by index | ✅ Implemented |
| Array range | `$.items[1:3]` | Elements at indices 1 and 2 (exclusive end) | ✅ Implemented |
| Open-start range | `$.items[:3]` | First three elements | ✅ Implemented |
| Open-end range | `$.items[2:]` | Elements from index 2 onwards | ✅ Implemented |
| Wildcard (array) | `$.items[*]` | All array elements | ✅ Implemented |
| Wildcard (object) | `$.obj.*` | All property values | ✅ Implemented |
| Recursive descent | `$..propertyName` | Property at any depth | ✅ Implemented |
| Recursive wildcard | `$..*` | All nodes at any depth | ✅ Implemented |
| Negative index | `$.items[-1]` | Last element | ✅ Implemented |
| Negative range | `$.items[-2:]` | Last two elements | ✅ Implemented |
| Union indices | `$.items[0,2,4]` | Elements at indices 0, 2 and 4 | ✅ Implemented |
| Union properties | `$.obj[name,age]` | Multiple named properties | ✅ Implemented |
| Existence filter | `$.items[?(@.isbn)]` | Elements that have a property | ✅ Implemented |
| Comparison filter | `$.items[?(@.price < 10)]` | Elements matching a comparison | ✅ Implemented |
| Logical filter | `$.items[?(@.p > 1 && @.p < 5)]` | Filters with `&&` / `\|\|` | ✅ Implemented |
| Computed index expression | `$.items[(@.length-1)]` | Index from expression evaluated against array length | ✅ Implemented |

## API reference

```csharp
// Returns the first matching node, or null if not found.
Task<JsonNode?> ExtractFirstJsonMatchAsync(this Stream stream, string? selectToken)

// Returns an async sequence of all matching nodes.
IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(this Stream stream, string? selectToken)
```

Passing `null` or `"$"` as `selectToken` returns the entire document.

## Implementation roadmap

The section below details the remaining planned work. All earlier phases (negative/union indexing, filter expressions, computed index expressions) are implemented.

### Phase 1 — True streaming (no full-document parse)

> **Goal:** Eliminate the `JsonNode.ParseAsync` call so large streams are traversed without holding the whole document in RAM.

| Feature | Notes |
|---|---|
| Token-by-token path navigation | Introduce a streaming navigator that reuses `JsonPathParser` output and walks `Utf8JsonReader` tokens |
| Streaming multi-match | Yield each match as it is found; suitable for very large arrays |
| Memory cap option | Accept a `maxNodeBytes` threshold; fall back to full parse above it |

**Implementation sketch:**
1. Add a dedicated streaming matcher (for example `JsonPathStreamingMatcher`) that evaluates parsed `JsonPathSegment` lists against `Utf8JsonReader`.
2. Keep `JsonPathMatcher` as the in-memory strategy and select between strategies in `StreamJsonExtractionExtensions`.
3. Add compatibility tests to ensure streaming and in-memory strategies return the same results for identical paths.
4. Add benchmark comparing streaming vs full-parse for various document sizes.

---

## JSONPath ↔ XPath quick reference

The table below maps common XPath expressions to their JSONPath equivalents as a reference. Entries marked *"not present in the original spec"* are extensions defined by the JSONPath-Plus JavaScript library and are not guaranteed to be implemented in this library.

| XPath | JSONPath | Result | Notes |
|---|---|---|---|
| `/store/book/author` | `$.store.book[*].author` | The authors of all books in the store | Can also be represented without `$.` as `store.book[*].author` (not present in the original spec); `$` and `@` require escaping |
| `//author` | `$..author` | All authors | |
| `/store/*` | `$.store.*` | All things in store (books array + bicycle object) | |
| `/store//price` | `$.store..price` | The price of everything in the store | |
| `//book[3]` | `$..book[2]` | The third book | |
| `//book[last()]` | `$..book[(@.length-1)]` / `$..book[-1:]` | The last book | Use `[(@['...'])]` for special-char properties (not present in the original spec) |
| `//book[position()<3]` | `$..book[0,1]` / `$..book[:2]` | The first two books | |
| `//book/(category,author)` (XPath 2.0) | `$..book[0][category,author]` | Categories and authors of all books | |
| `//book[isbn]` | `$..book[?(@.isbn)]` | Filter books with an ISBN | Use `[?@['...']]` for special-char properties (not present in the original spec) |
| `//book[price<10]` | `$..book[?(@.price<10)]` | Filter books cheaper than 10 | |
| `//*[name()='price' and . != 8.95]` | `$..*[?(@property === 'price' && @ !== 8.95)]` | Property values of objects whose property is price and not 8.95 | Add `^` after expression to get the parent object |
| `/` | `$` | Root of the JSON object | Backtick-escape to match a literal `$` |
| `//*/*|//*/*/text()` | `$..*` | All elements/members beneath root | |
| `//*` | `$..` | All elements/parent components including root | Not directly specified in the original spec |
| `//*[price>19]/..` | `$..[?(@.price>19)]^` | Parent of items with price > 19 | Parent (caret) not present in the original spec |
| `/store/*/name()` (XPath 2.0) | `$.store.*~` | Property names of store sub-object | Property name (tilde) not present in the original spec |
| `/store/book[not(. is /store/book[1])]` (XPath 2.0) | `$.store.book[?(@path !== "$['store']['book'][0]")]` | All books except the first | `@path` not present in the original spec |
| `//book[parent::*/bicycle/color="red"]/category` | `$..book[?(@parent.bicycle && @parent.bicycle.color === "red")].category` | Categories of books whose parent has a red bicycle | `@parent` not present in the original spec |
| `//book/*[name() != 'category']` | `$..book.*[?(@property !== "category")]` | All children of "book" except "category" | `@property` not present in the original spec |
| `//book[position() != 1]` | `$..book[?(@property !== 0)]` | All books except the first | `@property` not present in the original spec |
| `/store/*/*[name(parent::*) != 'book']` | `$.store.*[?(@parentProperty !== "book")]` | Grandchildren of store whose parent isn't "book" | `@parentProperty` not present in the original spec |
| `//book[count(preceding-sibling::*) != 0]/*/text()` | `$..book.*[?(@parentProperty !== 0)]` | Property values of all non-first book instances | `@parentProperty` not present in the original spec |
| `//book[price = /store/book[3]/price]` | `$..book[?(@.price === @root.store.book[2].price)]` | Filter books with price equal to the third book | `@root` not present in the original spec |
| `//book/../*[. instance of element(*, xs:decimal)]` (XPath 2.0) | `$..book..*@number()` | Numeric values within the book array | `@number()`, `@boolean()`, `@string()`, `@null()`, `@object()`, `@array()`, `@integer()`, `@scalar()`, `@other()`, `@undefined()`, `@function()`, `@nonFinite()` not present in the original spec |
| `//book/*[name()='category' and matches(., 'tion$')]` (XPath 2.0) | `$..book.*[?(@property === "category" && @.match(/TION$/i))]` | Categories of books matching regex (ends in 'TION') | `@property` not present in the original spec |
| `//book/*[matches(name(), 'bn$')]/parent::*` (XPath 2.0) | `$..book.*[?(@property.match(/bn$/i))]^` | Books with a property matching regex (ends in 'bn') | Uses parent selector `^` to return to the parent object |
| | `` ` `` (e.g., `` `$ ``) | Escapes the following sequence as a literal | `` ` `` not present in the original spec; use ``` `` ``` for a literal backtick |


## License

MIT
