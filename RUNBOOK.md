# Running from scratch

Nothing here assumes a previous session. Follow it in order, top to bottom.

## 0. Prerequisites

You need the .NET 10 SDK, Node, Docker CLI, Colima and Ollama.

```bash
dotnet --version        # 10.0.401 or newer
node --version
docker --version
colima version
ollama --version
```

Install anything missing:

```bash
brew install dotnet node colima
# Ollama: https://ollama.com/download
```

If Ollama lives outside Homebrew on Apple Silicon, its binaries are usually already on your PATH. If `ollama` is not found, run it from wherever it installed, or add it:

```bash
export PATH="/opt/homebrew/opt/ollama/bin:$PATH"
```

## 1. Start Qdrant

Qdrant is the vector database. It runs as a container inside Colima.

```bash
colima start --cpu 2 --memory 4 --disk 20
```

First run only, create the container:

```bash
docker run -d --name dpa-qdrant \
  -p 6333:6333 -p 6334:6334 \
  -v dpa_qdrant:/qdrant/storage \
  qdrant/qdrant:latest
```

Check it:

```bash
curl -s http://127.0.0.1:6333/collections/dpa_sections_minilm
```

An empty collection returns a JSON error about the collection not existing, which is expected on a first run. A working Qdrant answers on port 6333.

## 2. Start Ollama

This is the local model. It is the unlimited fallback, so the app never dies when a hosted provider is rate limited.

```bash
ollama serve
```

Leave that running. In a second terminal:

```bash
ollama pull qwen2.5:7b
```

Roughly 4.7 GB, one time only.

Check it:

```bash
curl -s http://127.0.0.1:11434/api/version
```

## 3. Build

```bash
cd ~/Desktop/dpa-rag
dotnet build
(cd web && npm install && npm run build)
```

The client build writes into `src/Dpa.Rag.Api/wwwroot`, which is where the API serves it from.

## 4. Run

You need at least one model key. Any one of these is enough, and they stack.

```bash
export GROQ_API_KEY=<your groq key>
export DPA_RAG_LLM_KEY=<your nvidia key>
```

Ollama needs no key at all.

```bash
dotnet run --project src/Dpa.Rag.Api
```

On the first run it embeds all 45 sections, writes the cache, and upserts into Qdrant. That takes a few seconds. Later runs load the cache.

```
llm chain: groq/qwen/qwen3.8-27b then groq/openai/gpt-oss-120b then nvidia/z-ai/glm-5.3-flash then ollama/qwen2.5:7b
loaded 45 sections from .../data/section_index.json
vector store: qdrant 127.0.0.1:6334 / dpa_sections_minilm
qdrant seeded: 45 points
```

Open <http://127.0.0.1:5199>.

## 5. Verify before you demo

The fast tier needs no model and no network. Run it in front of them if you like.

```bash
bash scripts/verify_all.sh
```

Finishes in about six seconds. Expect `Passed: 50`, zero warnings, and four PASS lines from the clean gate.

Confirm the vector database really holds the data:

```bash
curl -s http://127.0.0.1:6333/collections/dpa_sections_minilm
```

Look for `points_count` of 45, `size` of 384 and `distance` of `Cosine`.

The slow tier drives a real model and takes minutes. Do it before the demo, not during it.

```bash
# terminal 1
LLM_CHAIN=ollama ASPNETCORE_URLS=http://127.0.0.1:5310 \
  dotnet run --project src/Dpa.Rag.Api

# terminal 2
CHAT_URL=http://127.0.0.1:5310/api/chat \
STRESS_DEADLINE=2400 STRESS_WORKERS=2 \
  python3 scripts/stress_test.py
```

It writes progress to `tmp/stress.log` as it goes, so you can watch it with `tail -f tmp/stress.log`. Use `STRESS_WORKERS=2`. A local 7B on 16 GB cannot absorb more than that and the extra requests time out and look like failures.

## 6. Stop everything

```bash
pkill -f "Dpa.Rag.Api"
pkill -f "ollama serve"
colima stop
```

## Troubleshooting

**`QdrantVectorStore` fails to connect.** Colima is not up, or the container is not running. Check `docker ps`. If the container exists but is stopped, `docker start dpa-qdrant`.

**`llm chain pinned` message.** That means `LLM_CHAIN` is set. Unset it for a normal run.

**`503` on every question.** Every provider failed. With no key set, only Ollama is available, so check `ollama ps` and make sure `qwen2.5:7b` is pulled.

**Answers are slow.** Groq is the fast path at roughly half a second. Once its daily quota is spent the chain falls to the local model at five to fifteen seconds. That is expected and not a fault.

**Empty or wrong search results.** Confirm the seed line printed `qdrant seeded: 45 points`. If the vector index was rebuilt with a different embedding model, delete the cache and let it rebuild.

```bash
rm data/section_index.json
dotnet run --project src/Dpa.Rag.Api
```