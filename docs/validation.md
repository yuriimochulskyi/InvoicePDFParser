# Validation: Parsed vs NeedsReview

The model never decides whether an invoice is correct. The final status is set in code by
`InvoiceReviewPolicy.Decide`, which looks at an `ExtractionRun` and returns either `Parsed` or
`NeedsReview` together with a human-readable reason.

## How ValidateTotals works

`TotalsValidator.Validate` is pure arithmetic with a tolerance of 0.01. It checks three things:

1. For every line item that has quantity, unit price and amount: `quantity × unitPrice = amount`
   (rounded to two decimals).
2. If a subtotal is given: the sum of line amounts equals the subtotal.
3. `baseAmount + taxAmount − discountAmount = total`, where `baseAmount` is the sum of line items, or the
   subtotal when there are no line items. Missing tax or discount count as zero.

A receipt with neither line items nor subtotal cannot be verified and fails with
"no line items and no subtotal, total cannot be verified".

The same method is exposed to the agent as the `ValidateTotals` tool, so the model can catch its own
misreads during turn 1, and is run again by the policy afterwards. The tool has a budget of three calls per
run; after that it tells the model to stop and answer with the values as printed.

## The review rules

A run is `NeedsReview` when any of these hold:

- The run ended with an error (model timeout, invalid JSON, invalid output).
- The agent never called `ExtractPdfText`, or the raw text is empty: the output is not grounded.
- The agent returned no invoice.
- Required fields are missing: `vendorName`, `invoiceNumber`, `invoiceDate`, `currency`, `total`.
- `invoiceDate` or `dueDate` is not `yyyy-MM-dd`, or `currency` is not a three-letter ISO 4217 code.
- Amount grounding fails: `subtotal`, `taxAmount`, `discountAmount` or `total` does not literally appear
  in the document text. `AmountGrounding` parses every number in the text in several locale formats
  ("1.558,78", "1,558.78", "13 700,00") so a model that bends a figure, or invents a 0.00 tax, is caught.
- The totals check fails.

Otherwise the status is `Parsed`. Reasons are joined with `; ` and stored with the record, so a reviewer sees,
for example, "totals check failed: lines 1309.90 + tax 248.88 - discount 0.00 = 1558.78, but total is
1585.78 (diff 27.00)".

Before any model call, `DocumentPreflight` also sends two cases straight to review: a PDF with no text layer
when OCR is not configured or failed, and a document longer than `Ai:MaxDocumentChars` (default 14000).
