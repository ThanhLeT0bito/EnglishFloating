# AI Providers Design

## Goal

Let the learner choose which AI service powers every AI feature (vocabulary explanations, Add with AI phrasing, Writing practice): Gemini (default), Groq (free tier, very fast) or Claude (paid API). Also fix slow Gemini responses.

## Behaviour

- Settings > **AI** (formerly Gemini): an "AI provider" selector (Gemini / Groq (free, fast) / Claude (paid API)) plus one key card per provider. A warning shows when the selected provider has no key. Keys and the provider apply on **Save changes**, like other settings.
- Keys are stored per provider with DPAPI in separate files (`gemini.dpapi`, `groq.dpapi`, `anthropic.dpapi`) and only sent to their own provider. Settings JSON stores only `AiProvider` and `*ApiKeyConfigured` flags.
- Every request goes to the provider selected in saved settings at the time of the call.

## Request contract (all providers)

- One system instruction (rules), one user message (data, never instructions), JSON output against the app's schema.
- Each model gets its own 20 s slot inside a 45 s total budget; a slow or transient model (404/429/500/502/503/504/529) hands over to the next model. HTTP 400 retries the same model once with a simpler "relaxed" request. 401/403 report a rejected key. Responses are capped at 64 KB; a surrounding markdown fence is removed. Messages name the provider and never contain the key.

| Provider | Endpoint | Models (in order) | Structured output | Relaxed retry |
|---|---|---|---|---|
| Gemini | generateContent | 3.8 / 3.6 / 3.5 Flash, 3.5 Flash-Lite | `responseSchema` + `thinkingLevel: "low"` | drop thinking level |
| Groq | OpenAI-compatible chat completions | `openai/gpt-oss-120b`, `llama-3.3-70b-versatile` | strict `json_schema` (gpt-oss, `reasoning_effort: "low"`), `json_object` otherwise | `json_object` + schema in prompt |
| Claude | Messages API (`anthropic-version: 2023-06-01`) | `claude-haiku-5-5`, `claude-haiku-4-5` | `output_config.format` json_schema | schema in system prompt |

Gemini-style schemas are converted to strict JSON Schema (lower-case types, `additionalProperties: false`, all properties required) for Groq and Claude.

## Components

- Core `AiProvider`, `AiProviderRules`; settings fields, merger and JSON normalization (unknown values → Gemini).
- Windows `IJsonLlmClient`, `JsonLlmClientBase` (shared loop), `GeminiJsonClient`, `OpenAiCompatibleJsonClient` (Groq), `AnthropicJsonClient`, `JsonSchemaConverter`, `SwitchingJsonClient`.
- `AiVocabularyExplainer`, `AiSentencePhraser`, `AiWritingCoach` replace the Gemini-only services and take an `IJsonLlmClient`.

## Out of scope

Choosing models per provider in the UI, more OpenAI-compatible providers (OpenRouter, Cerebras: the client already takes an endpoint), usage/cost display.
