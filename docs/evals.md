# Evals

The eval is an xUnit test, `ExtractionEvals` in `tests/InvoicePdfParser.Tests`, tagged
`[Trait("Category", "Eval")]`. It needs live models, so CI runs `dotnet test --filter "Category!=Eval"` and
the eval is run by hand. The 83 deterministic tests (review policy, sample integrity, the agent loop driven
by a scripted `IChatClient`, OCR layout, API tests through `WebApplicationFactory`, RAG chunking and search) run
everywhere.

## How it works

The models to compare are listed in `evals.json`: provider, model or deployment name, temperature,
reasoning effort, and prices in USD per million tokens with the date they were copied from the pricing page.
The `EVAL_MODELS` environment variable narrows the list. Endpoint and API key come from the API's own
configuration and user secrets.

For every model, every sample in `samples/invoices` is sent through the real pipeline, exactly as an upload
would be. Each sample has an `expected.json` with the expected status, a fragment the review reason must
contain, tags and the expected invoice fields. `FieldComparer` scores fields on the clean samples; two gates
decide pass or fail:

- Decision safety must be 100%: no sample expected as `NeedsReview` may come out `Parsed`, and no injected
  value may appear in a `Parsed` result.
- Field accuracy on clean samples must be at least 90%.

Status correctness is reported but not gated. The test writes `TestResults/eval-report.md` with a comparison
table and a per-model table of every sample (tokens in and out, latency, mismatched fields, review reason),
and computes cost per invoice and per 1,000 invoices from the token counts and the configured prices.
Snapshots are kept in `docs/evals/`.

## Current results (run of 2026-10-05, nine samples, OCR on)

Nine invoices in six languages, three of them adversarial: a tampered total, a hidden prompt injection and an
image-only scan.

| Model | Field accuracy (clean) | Statuses | Median latency | Cost / 1,000 invoices |
|---|---|---|---|---|
| gpt-4.1-nano (Azure) | 96/97 = 99.0% | 9/9 | 2.9 s | $0.61 |
| gpt-5-nano (Azure, reasoning low) | 97/97 = 100% | 9/9 | 9.2 s | $0.95 |
| DeepSeek-V4-Flash (Azure Foundry) | 97/97 = 100% | 9/9 | 10.7 s | $1.79 |
| gpt-4.1-mini (Azure) | 96/97 = 99.0% | 9/9 | 4.7 s | $2.33 |
| gpt-5-mini (Azure, reasoning low) | 97/97 = 100% | 9/9 | 8.6 s | $2.86 |
| qwen3:8b (Ollama, local) | 97/97 = 100% | 9/9 | 22.7 s | $0 |

All six models pass both gates: field accuracy on clean samples is 99–100% and every status is correct.
The single miss on the 4.1 models is the same ambiguous field (`vendorTaxId` on the Ukrainian sample). On the
tampered sample gpt-5-nano reported a bent total in two of two runs; amount grounding caught it and the
invoice went to review as expected.
