Eval: Ollama / qwen3:8b, 2026-09-28 15:37

| File | Status | Fields correct | Latency | Mismatched fields | Review reason |
|---|---|---|---|---|---|
| 01-ua-fop | Parsed | 13/14 | 28.7 s | vendorTaxId | — |
| 02-de-rechnung | Parsed | 14/14 | 15.6 s | — | — |
| 03-us-invoice | Parsed | 14/14 | 15.0 s | — | — |
| 04-uk-multipage | Parsed | 29/29 | 71.5 s | — | — |
| 05-pl-discount-shipping | Parsed | 15/15 | 22.4 s | — | — |
| 06-ch-receipt | NeedsReview | 11/11 | 9.3 s | — | totals check failed: no line items and no subtotal, total cannot be verified |

**Overall field accuracy: 96/97 = 99.0 %** (required ≥ 80 %)
