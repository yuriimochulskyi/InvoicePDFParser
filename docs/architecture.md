# Architecture

The solution `InvoicePdfParser.sln` has two projects. `src/InvoicePdfParser.Api` is an ASP.NET Core
(.NET 10) web API; `tests/InvoicePdfParser.Tests` holds xUnit tests and the model evals.

## Folders of the API project

- `Agents` — the LLM-facing part: `InvoiceExtractionAgent` (a `ChatClientAgent` from Microsoft Agent
  Framework over an `IChatClient`), `InvoiceTools` (the two tools), `ChatClientFactory` (builds one
  `OpenAI.OpenAIClient` for Ollama or Azure), `ExtractionRun` (what one run produced: invoice, tool calls,
  raw text, tokens, latency, error).
- `Documents` — getting text out of an uploaded file: `PdfFileStore`, `PdfTextExtractor` (PdfPig, rebuilds
  table rows with ` | ` between columns), `OcrFallbackTextSource` and `AzureDocumentIntelligenceOcr` for
  scans, `DocumentPreflight`.
- `Validation` — deterministic rules: `TotalsValidator`, `AmountGrounding`, `InvoiceReviewPolicy`.
- `Pipeline` — `InvoiceProcessingService` wires store → text → preflight → agent → policy → database.
- `Configuration` — `AiOptions` (provider, per-model sampling, OCR, limits) and DI registration.
- `Controllers`, `Models`, `Data` — the HTTP surface (`POST /api/invoices`, `GET /api/invoices/{id}`),
  DTOs and the EF Core + SQLite `InvoiceRecord`.
- `Rag` — a small retrieval module over the markdown docs of this repository, exposed as `POST /api/ask`.

## The agent loop

The agent runs in two turns within one session. In turn 1 the model gets the `fileId` and the tools, with
no response format. It must call `ExtractPdfText(fileId)`, which returns the document text wrapped in
`<document>…</document>` and marked as untrusted. It then drafts the invoice and calls `ValidateTotals` with
typed numeric parameters (line items, subtotal, tax, discount, total); on a mismatch it re-reads and tries
again, up to a budget of three checks. Tool iterations are capped at 8 per request.

In turn 2 the agent is asked for the final answer with `ToolMode.None` and the `InvoiceDto` JSON schema as
structured output, so this turn only formats what turn 1 found. The two-turn design exists because Ollama
applies a response schema to every turn, which made the model skip tools and invent an invoice.

Every run records provider, model, tokens, latency, the list of tool calls and the raw document text. The
review policy uses the tool-call list as its grounding signal: a run that never called `ExtractPdfText` is
sent to review regardless of how plausible its output looks.
