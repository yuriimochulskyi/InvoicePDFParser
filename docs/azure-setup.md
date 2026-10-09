# Switching from Ollama to Azure OpenAI / Foundry

The default provider is Ollama with `qwen3:8b` on `http://localhost:11434`. Both providers speak the OpenAI
Chat Completions protocol, so `ChatClientFactory` builds one `OpenAI.OpenAIClient` and exposes it as an
`IChatClient`. Ollama is reached through its `/v1` endpoint, an Azure AI Foundry resource through
`/openai/v1/`. The agent code does not change; only the `Ai` configuration section does.

## Configuration

To switch to Azure OpenAI, set the provider and the Azure settings with user secrets. The API key never goes
into `appsettings.json`:

```powershell
cd src/InvoicePdfParser.Api
dotnet user-secrets set "Ai:Provider" "AzureOpenAI"
dotnet user-secrets set "Ai:AzureOpenAI:Endpoint" "https://<resource>.services.ai.azure.com/"
dotnet user-secrets set "Ai:AzureOpenAI:Deployment" "gpt-4.1-mini"
dotnet user-secrets set "Ai:AzureOpenAI:ApiKey" "<key>"
```

`Ai:Provider` accepts `Ollama` or `AzureOpenAI`. `Deployment` is the deployment name in Foundry; the same
endpoint serves OpenAI deployments and third-party models such as DeepSeek-V4-Flash, so switching between
them is only a change of this name.

## Sampling settings per model family

Sampling is configuration per provider, because it depends on the model family:

```json
"Ollama":      { "Model": "qwen3:8b", "Temperature": 0, "ReasoningEffort": "None" },
"AzureOpenAI": { "Deployment": "gpt-4.1-mini", "Temperature": 0, "ReasoningEffort": null }
```

Classic models (`gpt-4.1-mini`, `gpt-4.1-nano`, DeepSeek) take `Temperature: 0` for repeatable extraction.
Reasoning models (`gpt-5-mini`, `gpt-5-nano`, o-series) reject the temperature parameter with HTTP 400, so
for them set `Temperature` to `null` and `ReasoningEffort` to `Low`. For qwen3 on Ollama `ReasoningEffort:
None` switches off thinking, which otherwise multiplies latency without improving extraction.

## Validation at startup

`AiOptions.Validate()` runs when the host starts. A missing Azure endpoint, key or deployment, an invalid
Ollama URL, a temperature outside 0–2 or a timeout outside 10–3600 seconds stops the application with the
list of problems instead of failing the first upload.

## Optional OCR

Scanned PDFs are recognised with Azure AI Document Intelligence (`prebuilt-read`) when both
`Ai:DocumentIntelligence:Endpoint` and `Ai:DocumentIntelligence:ApiKey` are set, also via user secrets.
Without them a PDF with no text layer goes to review before any model call. Other settings:
`Ai:RunTimeoutSeconds` (default 300) and `Ai:MaxDocumentChars` (default 14000).
