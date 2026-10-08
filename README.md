# Data Privacy Act of 2012 Assistant

A retrieval augmented chatbot that answers questions about Republic Act No. 10379, the Data Privacy Act of 2012 of the Philippines. Every answer is retrieved from the text of the Act and cites the sections it used. Questions the Act does not cover are declined rather than answered from memory.

## What it does

- Parses the official PDF of the Act into 45 numbered sections, each with its chapter heading.
- Embeds every section locally with `all-MiniLM-L6-v2` running on ONNX Runtime. No embedding API, no cost, no rate limit.
- Scores a question with a hybrid of dense cosine similarity and IDF weighted lexical overlap, then blends the two.
- Sends only the retrieved sections to the LLM and instructs it to cite them and never invent a section number.
- Strips any section number the model cites that was not actually supplied as evidence.
- Declines when the top retrieval score falls below a relevance floor.
- Streams the answer, the sections used, and a relevance score to a single page client.

## Architecture

```
data/source/dpa_npc.pdf        official PDF from the National Privacy Commission
        |
        |  tools/Dpa.Rag.Ingest
        v
data/dpa_articles.json         45 sections, parsed and committed
        |
        |  src/Dpa.Rag.Core, MiniLmEmbedder, ONNX Runtime
        v
data/section_index.json        cached vectors, built once on first run
        |
        |  src/Dpa.Rag.Core, ActIndex
        |    dense cosine blended with IDF weighted lexical overlap
        v
  top 8 sections
        |
        |  src/Dpa.Rag.Core, ActAnswerer
        |    system prompt forbids outside knowledge and invented sections
        v
  src/Dpa.Rag.Core, LlmClient
        |    deepseek-ai/deepseek-v4.1-flash, then z-ai/glm-5.3-flash on failure
        v
  src/Dpa.Rag.Api              POST /api/chat, GET /api/act, GET /api/health
        |
        v
  web                          React, Motion, Animate UI primitives, served from wwwroot
```

## Why these choices

**C# for the whole backend.** The role lists Node.js, Python, PHP, Java and C#. C# is the language the engineer already ships in, so every line is defensible without an explanation. The .NET equivalent of the Python RAG stack would have cost hours for no gain here.

**Embeddings run locally.** Every embedding model on the NVIDIA NIM endpoint was end of life when this was built: `nvidia/nv-embed-v1` returned HTTP 410 on 25 August 2026, `nvidia/nv-embedcode-7b-v1` and `baai/bge-m3` likewise. Running `all-MiniLM-L6-v2` through ONNX Runtime removes the dependency entirely. The 384 dimension vectors are computed once and cached, so the running system makes zero embedding calls.

**Retrieval is hand written.** A 45 section corpus does not need a hosted vector database. The index is a list of sections with a float array each, and scoring is a cosine loop. That is roughly one hundred and fifty readable lines, and it means there is no black box between a question and the sections it matched.

**Hybrid scoring.** Pure dense retrieval on a short legal text misses exact term matches. Pure lexical matching misses paraphrase. Dense similarity is blended at 55 percent against IDF weighted lexical overlap at 45 percent, where the IDF weight is computed over the corpus and the title carries 60 percent of the lexical signal. A definitional question gets a boost for Section 3, because the Act defines its terms there.

**Chunking is by section.** Legal text has natural boundaries and section numbers are the citation key. Splitting into fixed token windows would cut Section 12 in half and make citations meaningless.

**The LLM never sees the question alone.** It receives the question plus the retrieved sections and nothing else, which makes hallucination structurally harder. Any section number it cites that was not in the evidence is stripped and the answer is flagged as unreliable.

**Declining is a feature.** Three of the ten verification questions are outside the scope of the Act. A retrieval augmented system that cannot say no is not trustworthy for a legal question.

## Corpus provenance

Source: National Privacy Commission, `data/source/dpa_npc.pdf`, downloaded manually from the official agency site.

The publisher's file name reads `Republic Act 10173`, which is a typo on their side. The correct act number is **10379**. The document content is correct and the parsed text matches the enacted law.

The parser strips the site's navigation chrome, repeated page headers, "Back To Top" links and the table of contents, then splits the body on `SEC. n.` and `SECTION n.` headings. All 45 sections are present in ascending order with no gaps, no chapter text leaking into section bodies, and no site furniture in the output.

## Running it

Requires the .NET 10 SDK.

```bash
export DPA_RAG_LLM_KEY=<key>
dotnet run --project src/Dpa.Rag.Api
```

Then open `http://127.0.0.1:5199`. The first run builds the vector index from `data/dpa_articles.json` and takes about a second. Later runs load the cached index.

To rebuild the corpus from the PDF:

```bash
dotnet run --project tools/Dpa.Rag.Ingest -- data/source/dpa_npc.pdf data/dpa_articles.json
rm data/section_index.json
```

To rebuild the client:

```bash
cd web && npm install && npm run build
```

## Verification

```bash
dotnet test
python3 scripts/verify_retrieval.py
```

`verify_retrieval.py` asks ten questions against a running server and fails the run on three conditions: a cited section that was not supplied as evidence, an answerable question that was declined, or an unanswerable question that was not. All ten pass.

## Layout

```
src/Dpa.Rag.Core      parser, embedder, index, answerer, LLM client
src/Dpa.Rag.Api       HTTP layer and static hosting
tools/Dpa.Rag.Ingest  PDF to sections
tests/Dpa.Rag.Tests   parser unit tests
web                   client
models                MiniLM ONNX weights and vocabulary, 86 MB
data                  source PDF, parsed corpus, cached vectors
scripts               verification harness
```

## Known limits

- Latency on the primary free model is 10 to 20 seconds per answer. The secondary model is usually faster.
- The relevance floor and the blend weights are tuned against this ten question harness, not a larger benchmark.
- No persistence. Every question is answered fresh, with no conversation memory.
- No rate limiting on the API. It is a local demonstration surface.
- The Act text is the 2012 law only. Implementing Rules and subsequent amendments are not included.
