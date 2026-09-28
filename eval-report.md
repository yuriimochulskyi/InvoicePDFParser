Eval: Ollama / qwen3:8b, 2026-09-28 15:24

| File | Status | Fields correct | Latency | Mismatched fields | Review reason |
|---|---|---|---|---|---|
| 01-ua-fop | Parsed | 13/14 | 21.0 s | vendorTaxId | — |
| 02-de-rechnung | Parsed | 14/14 | 15.7 s | — | — |
| 03-us-invoice | Parsed | 14/14 | 15.2 s | — | — |
| 04-uk-multipage | Parsed | 29/29 | 64.9 s | — | — |
| 05-pl-discount-shipping | Parsed | 15/15 | 21.3 s | — | — |
| 06-ch-receipt | NeedsReview | 11/11 | 10.1 s | — | totals check failed: no line items and no subtotal, total cannot be verified |

**Overall field accuracy: 96/97 = 99.0 %** (required ≥ 80 %)
