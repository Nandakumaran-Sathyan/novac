# AI assistant (local LLM)

Status: **Phase 4 of 7 done**: `POST /api/ai/chat` answers with the local model, and the eight read-only store tools exist and are tested, but they are **not yet offered to the model** (Phase 5). Until then it cannot see any sales or stock and says so.

```
Angular  ──▶  ASP.NET Core API  ──▶  Ollama (127.0.0.1:11434)  ──▶  qwen3.5:9b
                  │
                  └──▶ read-only store tools ──▶ EF Core ──▶ PostgreSQL   (Phases 4-5)
```

The browser only ever talks to the API. Ollama listens on localhost only and is never exposed. The database is
**PostgreSQL** (the original brief said SQL Server; the project moved to PostgreSQL earlier, and the tools go
through EF Core, so they do not depend on the database engine).

## Setup (macOS)

```bash
brew install ollama
brew services start ollama          # background service, listens on 127.0.0.1:11434 only
ollama pull qwen3.5:9b              # 6.6 GB; the chat model
ollama pull embeddinggemma          # 621 MB; reserved for future RAG, not used yet
curl -s localhost:11434/api/version
```

Check from the API: `GET /health` lists `ai-model` (Healthy / Degraded), and `GET /api/ai/status` (signed in)
shows whether the model is reachable, installed and supports tool calling.

## Configuration (`Ollama` section)

| Key | Default | Notes |
|---|---|---|
| `BaseUrl` | `http://localhost:11434` | Must be a loopback address unless `AllowRemoteHost` is true. Startup fails otherwise. |
| `Model` | `qwen3.5:9b` | Models tagged `:cloud` / `-cloud` (e.g. `glm-5.1:cloud`, also present on this machine) are **refused at startup**: they would send shop data to a third party. |
| `TimeoutSeconds` | 120 | Per model call, including loading the model into memory (about 14 s cold). |
| `Temperature` | 0.2 | The model's own default is 1.0 with presence_penalty 1.5, tuned for chat, not for picking tools. |
| `ContextLength` | 8192 | `num_ctx`. The model supports 262K, but memory grows with it. |
| `Think` | false | See measurements below. |
| `KeepAlive` | `10m` | How long Ollama keeps the model loaded after a call. |

## Configuration (`Assistant` section)

| Key | Default | Notes |
|---|---|---|
| `MaxHistoryMessages` | 12 | Earlier turns passed to the model, newest first. |
| `MaxHistoryCharacters` | 8000 | Character budget for those turns. |
| `RequestsPerMinute` | 10 | Per signed-in user. In addition, each user can have only one question running at a time (429 otherwise). |

## Verified behaviour of qwen3.5:9b (Ollama 0.40.13, Apple M4, 16 GB)

Measured with real requests, not assumed from documentation.

- **Capabilities** (`/api/show`): completion, vision, tools, thinking. Ollama uses a dedicated `qwen3.5` parser,
  so tool calls come back structured, not as text to scrape. Q4_K_M quantisation, 9.7B parameters, 100% on the GPU.
- **Tool call format:** `message.tool_calls[] = { id, function: { index, name, arguments } }`, `arguments` is a
  **JSON object**, and `content` is empty. The client also accepts arguments as a JSON string, and treats
  anything else as `{}` so that argument validation (not the model) decides what happens.
- **Tool results** are sent back as `{ "role": "tool", "tool_name": "...", "content": "<json>" }`; the model then
  answers from them faithfully (it reproduced all five products and figures in the test).
- **Several tools in one turn** are supported (`GetTodaySales` and `GetLowStockProducts` together).
- **Thinking mode** adds a `message.thinking` field and made a tool-call turn about 45% slower (20.5 s vs 14.1 s).
- **Refusals:** it declined an unsupported action ("delete product 7"), an unsupported period ("2019"), and an
  attempt to pass `shopId=2` (the tools have no such parameter, and the server never accepts one anyway).

### The grounding problem, and the chosen prompt

With a mild system prompt ("never invent numbers"), a two-part question
("What are today's sales, and which products are low on stock?") produced **no tool call and invented data**
("$1250 with 45 bills", "Product A/B/C"). Results for the same question, two runs each:

| Prompt | Thinking | Called both tools | Time per turn |
|---|---|---|---|
| mild | off | **no (fabricated)** | 14 s |
| mild | on | 2 / 2 | 18-24 s |
| **strict rules** | **off** | **2 / 2** | **5-14 s** |
| strict rules | on | 2 / 2 | 16 s |

The assistant will use the strict-rules prompt with thinking off. Because two runs prove little, Phase 5 also
**enforces grounding in code**: an answer that states figures without any tool having run is not passed to the
user (it is retried once with a reminder, then replaced by an honest "I couldn't retrieve that").

### Performance

Output runs at about **7 tokens/s** on this machine, lower than this chip should manage, because the Mac was
under memory pressure during testing (swap 1.6 GB of 2 GB used, with VS Code, a browser, PostgreSQL and the API
open). A 150-token answer takes about 20 s, and a full question needs two model calls. Loading the model takes
about 14 s on the first request. Closing other apps, or keeping the model loaded (`KeepAlive`), helps; the chat UI
will stream the final answer (Phase 6) so users see progress.

## Security rules (all phases)

- The shop (tenant) comes **only** from the signed-in user's token. No tool takes a shop id, and the existing EF
  Core query filter limits every query to that shop. Tools run inside the request, where that filter is active.
- Tools are **read-only**. There is no free-form SQL, ever. Arguments are validated against a fixed set
  (named periods such as `thisMonth`, capped limits); dates are worked out by the server in the store time zone.
- The browser may send prior user/assistant text for context, but **never tool results**: data is fetched fresh
  on each question, so a forged "tool result" cannot feed the model fake figures.
- Message text and tool results (shop data) are **never written to logs**; logs record model, timings, token
  counts and which tools ran.
- Ollama stays on localhost; cloud models are refused; no secrets or credentials are ever put in a prompt.
- A future write action (none exist) will need explicit confirmation from the user in the UI.

## Phases

1. Inspect the project. **Done.**
2. Integrate Ollama with ASP.NET Core. **Done**: `IChatModelClient` (Application/AI), `OllamaChatClient` and
   `OllamaOptions` (Infrastructure/AI), startup validation, 503 mapping, `ai-model` health check,
   `GET /api/ai/status`.
3. Basic chat endpoint. **Done**: `POST /api/ai/chat` (`IAIService` / `AIOrchestrationService`), a server-owned
   system prompt (`AssistantPrompt`), validated requests, history limited to the newest 12 turns (user and
   assistant text only), a per-user limit of 10 questions a minute, and one question in progress per user.
   Live check against qwen3.5:9b with no tools connected: asked for today's sales, top products, yesterday,
   an outright "make up a number", and another shop's data, it refused every time and gave no figures.
4. Read-only store tools. **Done** (Application/AI/Tools): `StoreAITools` (the queries), `StoreToolRegistry`
   (schemas, argument checking, dispatch), `StorePeriod` (named periods). Checked on PostgreSQL against
   independent SQL: every figure matched.
5. Tool calling loop with the grounding guard.
6. Angular chat: dashboard panel and `/assistant` page.
7. Multi-shop security and correctness tests.

## Store tools

All read-only. None takes a shop, a user or a date: the shop comes from the token through the database's shop
filter, and dates come from a named period worked out by the server in the store's time zone.

| Tool | Arguments (defaults) | Source |
|---|---|---|
| GetTodaySales / GetYesterdaySales | none | The Reports page's daily-sales query: total paid, bills, average bill, tax. |
| GetTopSellingProducts | period (last30days), limit 1-20 (5), rankBy units or revenue (units) | The Reports page's top-products query. |
| GetMostProfitableProducts | period (last30days), limit 1-20 (5) | Same query, ranked by gross profit (net sales minus the cost recorded at sale time). |
| GetLowStockProducts | limit 1-50 (20) | The Inventory page's "Low" filter. |
| GetExpiringProducts | withinDays 1-365 (30), limit 1-50 (20) | Products expired or expiring within the window, soonest first. Per product, not per batch. |
| GetInventoryValue | none | Stock on hand x today's cost price and selling price. |
| GetUnsoldProducts | days 7-365 (30), limit 1-50 (20) | Active products with no completed sale in the window, most stock value first. |

Periods: `today`, `yesterday`, `last7days`, `thisMonth` (1st to today), `lastMonth` (the whole previous month),
`last30days`. There is no "sales for this month" tool yet; GetTodaySales and GetYesterdaySales cover the brief.

How a call is checked (`StoreToolRegistry`): an unknown tool or argument (for example a `shopId` the model made
up) is refused and logged, an invalid value (a period of "2019") is refused, and a number outside its range is
capped, with the cap reported in the result. Each result is wrapped as
`{ tool, arguments, data, adjustments? }`, so the model sees what was actually used; any failure becomes
`{ tool, error }` telling the model to say the data is unavailable, never a gap it could fill. Exception text is
never passed to the model.

## Future RAG (designed for, not built)

EmbeddingGemma (pulled, 2K-token context) for embeddings and Qdrant for vectors. Requirements fixed now:
every stored chunk carries the shop id in its payload, every search applies a **server-side shop filter** taken
from the token (never from the model or browser), and retrieval results are passed to the model the same way
tool results are. No placeholder code exists for it yet.
