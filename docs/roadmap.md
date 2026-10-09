# Production roadmap

What the prototype does not do yet, with the intended design for each item.

## Processing

- **Async processing.** Today `POST /api/invoices` processes synchronously and returns 201. The production
  shape is `202 Accepted` with a `Location` header and statuses `Queued → Processing → Parsed / NeedsReview /
  Failed`. First step: an in-process `Channel<Guid>` drained by a `BackgroundService`; then RabbitMQ
  (MassTransit) or Azure Service Bus with the same handler. Idempotency by content hash of the PDF.
- **Escalation.** When `NeedsReview` is caused by a model error (an ungrounded amount, a bad date format),
  retry once with a stronger deployment. Not for document limitations such as a receipt without line items
  or a scan OCR could not read; those stay with a human.
- **Human review.** `PUT /api/invoices/{id}/review` with the corrected `InvoiceDto`, reviewer and note;
  status `Reviewed`. Accepted corrections become new `expected.json` cases, so the eval set grows from real
  traffic.

## Document quality

- **OCR quality.** Use `prebuilt-layout` for real table structure instead of rows rebuilt from word
  coordinates; use per-word confidence as a review signal; add a local engine behind `IOcrEngine` for
  documents that may not leave the machine.
- **Vendor master and checksums.** Validate tax identifiers (ЄДРПОУ, NIP, USt-IdNr, EIN) against their
  check digits and a vendor master. This settles the РНОКПП/ІПН ambiguity on Ukrainian invoices and defeats
  an injected vendor name, which string-level grounding cannot catch today.

## Platform

- **Persistence and security.** SQL Server with EF Core migrations instead of `EnsureCreated` on SQLite;
  blob storage for PDFs; retention policies for `uploads/` and raw text; JWT bearer authentication against
  Entra ID; `rawText` visible to reviewers only.
- **Resilience and tracing.** `AddStandardResilienceHandler()` around the model client for retries and
  circuit breaking; `UseOpenTelemetry()` on the agent so each run produces `invoke_agent → chat →
  execute_tool` spans next to the Serilog logs.
- **Deployment.** Container image, health checks, and the eval run as a scheduled job that compares the
  current prompt and model against the last snapshot in `docs/evals/`.
