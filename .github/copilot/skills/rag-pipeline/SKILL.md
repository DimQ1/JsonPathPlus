# Skill: RAG Pipeline Implementation

## When to use
Invoke this skill when asked to implement, wire up, extend, or replace the RAG pipeline in this project.
Trigger phrases: "implement RAG", "wire up vector store", "integrate LLM", "real RAG answers", "replace stub", "add embeddings", "implement retrieval".

## Context

The stub lives in [`rag-chatbot-api/Controllers/RagController.cs`](../../../../rag-chatbot-api/Controllers/RagController.cs).
The contracts are fixed — do not change DTOs unless asked:
- Request: `RagQueryRequest { Question: string }` → [`Dtos/Rag/RagQueryRequest.cs`](../../../../rag-chatbot-api/Dtos/Rag/RagQueryRequest.cs)
- Response: `RagQueryResponse { Answer: string, Sources: RagSource[] }` → [`Dtos/Rag/RagQueryResponse.cs`](../../../../rag-chatbot-api/Dtos/Rag/RagQueryResponse.cs)
- `RagSource { Title: string, Url: string }`

The Angular frontend consumes this via [`rag-chatbot/src/app/core/services/rag.ts`](../../../../rag-chatbot/src/app/core/services/rag.ts) — no frontend changes needed unless the response shape changes.

## Implementation Workflow

### Step 1 — Clarify the stack
Before writing code, ask if not stated:
- **Vector store**: Azure AI Search, Qdrant, Chroma, pgvector, in-memory, or other?
- **Embeddings**: Azure OpenAI, OpenAI API, local (Ollama), or pre-computed?
- **LLM**: Azure OpenAI (GPT-4o/GPT-4), OpenAI API, Ollama, Semantic Kernel, or other?
- **Documents/knowledge base**: static files, a database, blob storage, or pre-indexed?

### Step 2 — Add NuGet packages
Install only what is needed for the chosen stack. Common choices:

| Stack | Packages |
|---|---|
| Azure OpenAI + Azure AI Search | `Azure.AI.OpenAI`, `Azure.Search.Documents`, `Microsoft.SemanticKernel` |
| OpenAI API | `OpenAI` (official) or `Microsoft.SemanticKernel` |
| Qdrant | `Qdrant.Client`, `Microsoft.SemanticKernel.Connectors.Qdrant` |
| Ollama (local) | `OllamaSharp` or Semantic Kernel Ollama connector |

### Step 3 — Add configuration
Bind secrets via the Options pattern (existing convention in this project):

1. Create `Options/RagOptions.cs`:
```csharp
public class RagOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;      // LLM model
    public string EmbeddingDeployment { get; set; } = string.Empty; // embedding model
    public string IndexName { get; set; } = string.Empty;           // vector store index
}
```

2. Add to `appsettings.Development.json` under `"Rag": { ... }`.

3. Register in `Program.cs`:
```csharp
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection("Rag"));
```

### Step 4 — Create IRagService + RagService
Follow the existing service pattern (`ITokenService` / `TokenService`):

1. `Services/IRagService.cs`:
```csharp
public interface IRagService
{
    Task<RagQueryResponse> QueryAsync(string question, CancellationToken ct = default);
}
```

2. `Services/RagService.cs` — implement retrieval → augmentation → generation:
   - **Retrieve**: embed `question`, search vector store for top-k chunks with metadata (title, url).
   - **Augment**: build a prompt: system message + retrieved chunks as context + user question.
   - **Generate**: call LLM, stream or await completion.
   - **Return**: `RagQueryResponse { Answer = completion, Sources = chunks.Select(...) }`.

3. Register in `Program.cs`:
```csharp
builder.Services.AddScoped<IRagService, RagService>();
```

### Step 5 — Wire into RagController
Replace the stub body:
```csharp
public class RagController(IRagService ragService) : ControllerBase
{
    private readonly IRagService _ragService = ragService;

    [Authorize]
    [HttpPost("query")]
    public async Task<ActionResult<RagQueryResponse>> Query(RagQueryRequest request, CancellationToken ct)
    {
        var response = await _ragService.QueryAsync(request.Question, ct);
        return Ok(response);
    }
}
```

### Step 6 — Document indexing (if needed)
If documents need to be indexed first:
- Create a separate `Services/IndexingService.cs` or a CLI tool / background job.
- Do **not** index at startup unless the collection is tiny and static.
- Add an Admin endpoint `POST /api/admin/index` (guarded by `[Authorize(Roles = "Admin")]`) if on-demand re-indexing is needed.

### Step 7 — Validate end-to-end
- Start both frontend and backend (`start: fullstack` VS Code task).
- Log into the app, navigate to `/chat`, send a question.
- Verify `Answer` and `Sources` are non-empty in the chat UI.
- Check API logs for any embedding/search/LLM errors.

## Quality Checklist
- [ ] Secrets are in `appsettings.Development.json`, not hardcoded.
- [ ] `RagOptions` is bound and validated at startup (`ValidateDataAnnotations()` or `ValidateOnStart()`).
- [ ] `IRagService` is registered as `Scoped` (not Singleton, as HttpClient-based clients should be scoped or use `IHttpClientFactory`).
- [ ] `CancellationToken` is threaded through all async calls.
- [ ] LLM prompt includes a system instruction to cite sources from the retrieved context only.
- [ ] `Sources` in the response map back to real documents (title + url from the vector store metadata).
- [ ] Error from LLM/vector store is caught and returns a meaningful `500` with `{ message }` (matching the project convention).

## Conventions to Respect
- Constructor injection via primary constructors, fields stored as `private readonly`.
- Return `ActionResult<T>` with `{ message }` error objects for non-2xx responses.
- Do not add EF Core migrations — if storing indexed document metadata, add a new `DbSet` to `AppDbContext` and drop + restart.
- Angular `RagService` and the chat UI require no changes as long as `RagQueryResponse` shape is unchanged.
