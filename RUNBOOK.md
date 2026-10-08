# Running this project

Everything below assumes a fresh machine and no prior session. If you just want
to demo it, read **Quick start** and stop.

---

## Quick start

One command. That is the whole demo path.

```bash
cd ~/Desktop/dpa-rag && make dev
```

It starts the vector database, starts the local model, loads your keys, prints
the link and opens your browser. You see:

```
  qdrant    up
  ollama    up
  groq      loaded
   open the chat -> http://127.0.0.1:5199
```

Type a question. With a key loaded the answer lands in about a second.

To stop everything:

```bash
make stop
```

It kills the whole process tree and tells you if anything is still listening.

---

## What you need installed

| Tool | Why | Get it |
| --- | --- | --- |
| .NET 10 SDK | runs the backend | `brew install dotnet` |
| Node.js 18 or newer | builds the client | `brew install node` |
| Docker CLI | talks to Qdrant | ships with Docker Desktop, or use Colima |
| Colima | runs the Docker daemon on macOS without Docker Desktop | `brew install colima` |
| Ollama | runs the local model | <https://ollama.com/download> |

Check everything at once:

```bash
dotnet --version && node --version && docker --version && colima version && ollama --version
```

### If `ollama` is not found

Homebrew installs it outside the default PATH on Apple Silicon. Either use the
full path, which the commands above already do:

```bash
/opt/homebrew/opt/ollama/bin/ollama
```

or add it once:

```bash
echo 'export PATH="/opt/homebrew/opt/ollama/bin:$PATH"' >> ~/.zshrc && source ~/.zshrc
```

---

## The two services

The app talks to two things that run on your machine. Neither is part of the
application process, and that is deliberate.

**Qdrant** is the vector database. It holds one point per section, 384
dimensions, cosine distance. It runs as a container.

**Ollama** is the language model. It runs locally so the app has a fallback that
cannot be rate limited or go down with an outage.

---

## Model keys

You need **no key at all** to run this. The local model answers on its own.

Add keys only to make it faster. Put them in `.env` in the repository root.
The file is ignored by git, so a key there can never be committed, and `make dev`
loads it for you. Never type a key into the terminal, where a stray character
turns it into an invalid key.

```bash
GROQ_API_KEY=...
DPA_RAG_LLM_KEY=...
```

```bash
chmod 600 .env
make secrets
```

`make secrets` prints `loaded` or `none` and never prints the key itself.

The chain tries Groq, then NVIDIA, then the local model. Each rung after the
first is optional, so any single key works and no key also works.

**Local model only**, expect roughly twenty seconds per answer. **With Groq**,
roughly one second.

---

## Everyday commands

Run these from the repository root.

| Command | What it does |
| --- | --- |
| `make infra` | start the vector database only |
| `make local` | start the local model only |
| `make open` | open the app in the browser |
| `make secrets` | show whether a cloud key is loaded |
| `make dev` | everything, the demo path, this is the one you need |
| `make test` | fast verification, about six seconds |
| `make check` | print what the vector database is holding |
| `make status` | show which services are up |
| `make stop` | stop everything |
| `make clean` | stop everything and clear local caches |
| `make help` | list every target |

---

## Verifying before a demo

Six seconds, no model call, no network:

```bash
make test
```

Expect fifty passing tests, zero build warnings, and four clean gate lines.

Then prove the vector database holds real data:

```bash
make check
```

Expect `points 45, 384 dim, Cosine, status green`.

If someone asks what is in the database, point them at the collection itself:

```bash
curl -s http://127.0.0.1:6333/collections/dpa_sections_minilm
```

---

## Slow verification

The end to end suite drives a real model and takes minutes, not seconds. Run it
before a demo, never during one.

```bash
# terminal 1
LLM_CHAIN=ollama ASPNETCORE_URLS=http://127.0.0.1:5310 dotnet run --project src/Dpa.Rag.Api

# terminal 2
CHAT_URL=http://127.0.0.1:5310/api/chat \
STRESS_DEADLINE=2400 STRESS_WORKERS=2 \
  python3 scripts/stress_test.py
```

Progress is written as it runs:

```bash
tail -f tmp/stress.log
```

Use `STRESS_WORKERS=2`. A local 7B model on 16 GB cannot take more concurrent
requests, and the excess time out and look like application failures.

---

## Rebuilding from source

```bash
dotnet build
(cd web && npm install && npm run build)
```

The client build writes into `src/Dpa.Rag.Api/wwwroot`, which is where the API
serves it from.

To re-parse the statute from the PDF:

```bash
dotnet run --project tools/Dpa.Rag.Ingest -- data/source/dpa_npc.pdf data/dpa_articles.json
rm data/section_index.json
dotnet run --project src/Dpa.Rag.Api
```

---

## Shutting down

```bash
make stop
```

---

## When something breaks

**"The vector database is not reachable"**

Qdrant is down. Run `make infra`, then run the app again. This happens whenever
Colima has been stopped, because the container stops with the virtual machine.

**"llm chain pinned to ollama" in the startup banner**

`LLM_CHAIN` is set in the environment. Unset it:

```bash
unset LLM_CHAIN
```

**Every answer is slow**

That is the local model, and it is the expected fallback. A fresh Groq quota
brings it back to roughly half a second.

**Every answer returns a service error**

No model is available. Check the local model is loaded:

```bash
ollama list
curl -s http://127.0.0.1:11434/api/version
```

**Search results look wrong**

The vector index may have been built with a different embedding model. Clear the
cache and let it rebuild on the next start:

```bash
make clean && make dev
```

**Port 5199 already in use**

Another instance is still running. `make stop`, then start again.

---

## Why the browser opens by itself

`make dev` waits a few seconds, then opens the link, because on a cold start the
app is still compiling when the command returns. Opening too early would land on
an empty tab.

If you prefer to open it yourself:

```bash
make open
```

`make dev` never needs a second terminal. Ollama and Colima both start in the
background.
