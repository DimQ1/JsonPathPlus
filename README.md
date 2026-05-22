# JsonPathPlus

[![NuGet](https://img.shields.io/nuget/v/JsonPathPlus?label=NuGet&logo=nuget)](https://www.nuget.org/packages/JsonPathPlus)
[![GitHub release](https://img.shields.io/github/v/release/DimQ1/JsonPathPlus?label=GitHub&logo=github)](https://github.com/DimQ1/JsonPathPlus/releases)
[![Live Demo](https://img.shields.io/badge/Live_Demo-🌐-blue)](https://dimq1.github.io/jsonpath-plus-online-evaluator/)

Lightweight JSONPath-like `Stream` extensions for `System.Text.Json`.

Extracts JSON subtrees from a `Stream` by path expression.
Uses streaming selectors for root arrays and root objects, with automatic full-parse fallback when a selector requires full-document evaluation.

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

// Union indices (multiple array elements)
await foreach (var item in stream.ExtractAllJsonMatchesAsync("$.items[0,2,4]"))
{
    Console.WriteLine(item);
}

// Union properties (multiple object fields)
JsonNode? selected = await stream.ExtractFirstJsonMatchAsync("$.book[title,author]");
Console.WriteLine(selected);
```

## NuGet publishing (GitHub only)

NuGet publishing is automated in GitHub Actions and does not require local package push commands.

Packages are published to **both** [nuget.org](https://www.nuget.org/packages/JsonPathPlus) and [GitHub Packages](https://github.com/DimQ1/JsonPathPlus/pkgs/nuget/JsonPathPlus).

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

1. Add GitHub Packages NuGet source (authenticated with `GITHUB_TOKEN`)
2. Restore
3. Build (Release) with /p:Version from branch name
4. Test
5. Pack with /p:PackageVersion from branch name
6. Push package to nuget.org with --skip-duplicate
7. Push package to GitHub Packages with --skip-duplicate
8. Create a GitHub Release automatically with `--generate-notes`

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
| Field inclusion projection | `$.books[title, author]` | Include only specified fields in result objects | ✅ Implemented |
| Field exclusion | `$.books[!title, !price]` | Exclude specified fields from result objects | ✅ Implemented |
| Nested query | `$.store[book[?(@.price<20)][title,author], bicycle[color], name]` | Apply sub-paths per key and combine into a result object | ✅ Implemented |

## API reference

```csharp
// Stream input
Task<JsonNode?> ExtractFirstJsonMatchAsync(this Stream stream, string? selectToken)
IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(this Stream stream, string? selectToken)

// Stream input with extraction options
Task<JsonNode?> ExtractFirstJsonMatchAsync(this Stream stream, string? selectToken, JsonPathExtractionOptions options)
IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(this Stream stream, string? selectToken, JsonPathExtractionOptions options)

// JsonNode input
Task<JsonNode?> ExtractFirstJsonMatchAsync(this JsonNode? node, string? selectToken)
IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(this JsonNode? node, string? selectToken)

// Raw JSON string input
Task<JsonNode?> ExtractFirstJsonMatchAsync(this string json, string? selectToken)
IAsyncEnumerable<JsonNode?> ExtractAllJsonMatchesAsync(this string json, string? selectToken)
```

Passing `null` or `"$"` as `selectToken` returns the entire document.

`JsonPathExtractionOptions.FullParseMaxBytes` lets you cap fallback full-document parsing by stream size. Streaming selectors are still allowed under this cap.

### Path validation

Use `JsonPathValidator` to check a path expression before executing it:

```csharp
// Quick boolean check
bool ok = JsonPathValidator.IsValid("$.items[?(@.price < 10)]");

// Full result with error message
JsonPathValidationResult result = JsonPathValidator.Validate("$.items[?()]");
if (!result.IsValid)
    Console.WriteLine(result.Error);
// → "Empty filter expression at position 7."
```

`Validate` detects the following structural errors:

| Problem | Example | Error |
|---|---|---|
| Unclosed `[` | `$.obj[p2,p1` | `Unclosed '[' at position 5.` |
| Empty filter body | `$.items[?()]` | `Empty filter expression at position 7.` |
| Filter missing `)` | `$.items[?(@.isbn]` | `Malformed filter expression at position 7: missing closing ')'.` |
| Empty computed index | `$.items[()]` | `Empty computed index expression at position 7.` |
| Computed index missing `)` | `$.items[(@.length-1]` | `Malformed computed index expression at position 7: missing closing ')'.` |

`null` and empty strings are considered valid (they select the root document). Bracket characters inside quoted string literals in filter expressions are handled correctly and do not produce false positives.

### Field projection and exclusion

**Field inclusion projection** selects only specified fields:

```csharp
// Returns only title, author, isbn fields
await foreach (var book in stream.ExtractAllJsonMatchesAsync("$.books[*][title, author, isbn]"))
{
    Console.WriteLine(book);
}
// Output: { "title": "...", "author": "...", "isbn": "..." }
```

**Field exclusion** removes specified fields from result objects:

```csharp
// Returns all fields except title and price
await foreach (var book in stream.ExtractAllJsonMatchesAsync("$.store.book[?(@.price > 2)][!title, !price]"))
{
    Console.WriteLine(book);
}
// Output: { "author": "J.R.R. Tolkien", "isbn": "978-0547928227", "category": "fiction" }
```

Both features work on individual objects and arrays of objects:

| Syntax | Example | Result |
|---|---|---|
| Single field exclusion | `$.books[0][!title]` | Book object without title field |
| Multiple field exclusion | `$.books[*][!title, !price, !meta]` | All books without title, price, meta fields |
| Exclusion with filters | `$.books[?(@.price > 10)][!author]` | Books over $10 without author field |
| Exclusion with ranges | `$.books[0:2][!isbn]` | First two books without isbn field |
| Exclusion on arrays | `$[*][!name]` | Array elements without name field |

**Behavior:**
- Exclusion only applies to objects. Arrays and scalar values are returned unchanged.
- A field exclusion segment is applied **after** all previous path segments complete.
- Excluding all fields returns an empty object `{}`.

### Nested query

**Nested queries** allow applying sub-paths to specific keys within a single bracket expression and combining the results into a result object:

```csharp
// Select books under $20 with title/author, bicycle color, and the store name
await foreach (var result in stream.ExtractAllJsonMatchesAsync(
    "$.store[book[?(@.price < 20)][title, author], bicycle[color], name]"))
{
    Console.WriteLine(result);
}
// Output: { "name": "Bookstore", "bicycle": { "color": "red" }, "book": [{ "title": "Book A", "author": "Author 1" }, ...] }
```

Each key in the bracket expression can have its own sub-path (filters, projections, exclusions, etc.) applied independently:

| Syntax | Example | Result |
|---|---|---|
| Simple key refs | `$[name, version]` | Object with name and version fields (parsed as field projection) |
| Key with sub-path | `$.store[book[?(@.price < 20)][title, author], name]` | Filtered books with title/author, plus store name |
| Key with projection | `$.data[users[name, email], config[theme]]` | Users with name/email, config with only theme |
| Non-existent key | `$.obj[missing[key], present]` | Key absent from result; only present returned |

**Behavior:**
- Keys that don't exist in the source object are omitted from the result.
- Sub-paths that match nothing cause the key to be omitted.
- A single sub-path match is kept as-is; multiple matches are collected into an array.
- Nested query is triggered when at least one branch has bracket-delimited sub-paths; otherwise, it falls through to existing segment types (field projection, property union, etc.).

## Malformed path behavior

The parser is lenient and never throws on invalid or incomplete path expressions. The table below documents the defined behavior for common malformed inputs.

| Malformed path | What happens | Result |
|---|---|---|
| `$.obj[p2,p1` | Unclosed `[` discards the bracket segment; preceding segments still match | Partial match up to the last valid segment |
| `$[name` | Unclosed `[` at root; no segments parsed | Entire document |
| `$.name.` | Trailing dot consumed silently | Same as `$.name` |
| `$..` | Double dot with empty property name; recursive segment skipped | Entire document |
| `""` | Empty string treated the same as `$` | Entire document |
| `$.items[?()]` | Filter body too short (3 chars minimum required); bracket segment discarded | The `items` node itself |
| `$.items[badkey]` | Bracket content is not a valid index, range, wildcard, filter, or union; discarded | The `items` node itself |
| `$.items[?@.isbn]` | Filter missing opening `(`; discarded | The `items` node itself |
| `$$` | Second `$` parsed as a literal property name; no such property | `null` |
| `$.   ` | Three-space string treated as a property name; no match | `null` |

**Guarantee:** no malformed path will cause an infinite loop or an unhandled exception. At worst, the path is parsed partially and matched against whatever valid segments were extracted before the error.

## Implementation roadmap

All planned roadmap phases in this README are implemented.

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
