# Personal AI Assistant (Local RAG)

A local document-question-answering prototype with an ingestion and retrieval pipeline written in **C# / ASP.NET Core (.NET 8)**.

**Next.js · Ollama · PostgreSQL / pgvector · Entity Framework Core**

The code exposes the mechanics: text extraction, overlapping chunks, embeddings, cosine ranking and context assembly. PostgreSQL stores the vectors; the current similarity calculation runs in C#.

[Run locally](#running-it) · [Pipeline](#how-it-works) · [Trade-offs](#whats-still-rough)

## Why I built it

I kept reading about retrieval-augmented generation and realised I could describe roughly what it did but couldn't have built one. That usually means I don't actually understand it. So I built one.

I also wanted it to run locally rather than against OpenAI's API, for two reasons. The obvious one is that the documents you most want a searchable assistant for are usually the ones you'd rather not upload somewhere — notes, contracts, anything with other people's information in it. The less obvious one is that when a system is free to run, you experiment with it. If every test query costs money, you stop poking at it, and poking at it is how you find out what it's actually doing.

## What it does

Upload a PDF, `.txt` or `.md` file, hit ingest, and the document gets split up, embedded and stored. Questions retrieve context from those documents and the prompt asks the model to answer from it. That instruction does not guarantee factual answers.

The document and model request path uses local services: the Next.js proxy forwards to the API on port 5146, and the API calls Ollama on port 11434. Dependencies, Docker images and models need downloading first; the frontend also uses build-time Google Fonts. Local inference is possible after that setup, but this repository does not provide a verified fully offline installation or network-isolation guarantee.

## Why I wrote the pipeline myself

I wrote the ingestion and retrieval in C# without a RAG orchestration framework, using PdfPig for PDF extraction, EF Core for persistence and HTTP clients for Ollama.

That meant I had to make each decision myself rather than accept a default: how to split the text, how many chunks to retrieve, what to do when nothing matched well. Those decisions are the actual substance of RAG, and using a library would have hidden every one of them from me.

## How it works

```
document  ->  split into chunks
          ->  each chunk through the embedding model (nomic-embed-text)
          ->  vectors stored in PostgreSQL with pgvector

question  ->  embedded with the same model
          ->  latest 500 candidates ranked by cosine similarity in C#
          ->  those chunks passed to the chat model (llama3.1:8b) as context
          ->  answer
```

The part that surprised me is how much the answer quality depends on the retrieval step rather than on the model. A good model given the wrong three paragraphs will confidently answer the wrong question. Most of the time I spent tuning this went into retrieval, not into prompting.

The embedding model and stored vectors are tied together. The database column is currently an unconstrained `vector`, so it does not enforce a fixed dimension. The C# dot product uses the shorter of two vector lengths rather than rejecting a mismatch. Changing the embedding model therefore requires re-ingesting the corpus; model identity and dimension validation are still missing.

The chat path uses the top five chunks and returns document IDs, original filenames, chunk indices and snippets. A short question containing “document”, “doc” or “file” narrows retrieval to the latest document. This heuristic makes a quick demo convenient, but can choose the wrong document for a real question.

## What's still rough

Being honest about where it is:

- **Chunking is heuristic.** It packs paragraphs where possible, then splits oversized text by character count with overlap (defaults: 1,200 characters and 150 overlap). It can still split sentences or tables, and PDFs need a text layer; there is no OCR.
- **Limited provenance.** Filename and chunk metadata are returned, but page/section positions and verified answer-to-source citations are not. A model-generated citation is not proof of support.
- **Bounded retrieval.** Only the latest 500 candidate chunks are considered, with no pgvector distance query or vector index. Older relevant chunks may be missed. There is no minimum relevance threshold or measured retrieval-quality benchmark.
- **Responses aren't streamed.** You wait for the whole answer, which on a local model is long enough to feel broken.
- **Single user.** There is no authentication or per-user document separation. Uploads and extracted text are stored locally without application-level encryption. Use synthetic documents for demos; this is not a hardened service for confidential material.

## Running it

Prerequisites: .NET 8 SDK, Node.js 20.9+ and npm, Docker with Compose, and [Ollama](https://ollama.com). Run these from the repository root.

Start Ollama (its desktop app or `ollama serve`), then fetch the exact models used by the services:

```bash
ollama pull llama3.1:8b
ollama pull nomic-embed-text
docker compose up -d
docker compose exec db pg_isready -U app -d personal_ai
```

Compose starts PostgreSQL 16 with pgvector on **localhost:55432**, database `personal_ai`. The matching demo connection string is in `appsettings.Development.json` under **`ConnectionStrings:Db`**, not in `appsettings.json`. Keep custom credentials out of tracked files; .NET also accepts `ConnectionStrings__Db` as an environment variable.

Create the schema using the existing EF migration (which also enables the vector extension):

```bash
dotnet tool install --global dotnet-ef --version 9.0.1
# If already installed, check `dotnet ef --version` and use a 9.x tool.
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update \
  --project backend/PersonalAiAssistant.Api

dotnet run --project backend/PersonalAiAssistant.Api --launch-profile http
```

The API runs on [localhost:5146](http://localhost:5146/swagger). The .NET target is 8; the checked-in EF Core packages are 9.0.1. Startup does not automatically apply migrations.

In a second terminal, from the repository root:

```bash
cd frontend
npm ci
npm run dev
```

Open [localhost:3000](http://localhost:3000), upload a synthetic document on the Documents page, click **Ingest**, then ask a question. Requests under `/api` are proxied to port 5146 by `frontend/next.config.ts`.

### Checks

```bash
dotnet build Personal-ai-assistent.sln
cd frontend
npm run lint
npm run build
```

There is no automated backend test project or RAG evaluation suite yet. Compilation does not verify answer quality or a full upload → ingest → retrieval flow.

Uploads and generated `bin/` / `obj/` output are excluded from version control. The API creates `Uploads/` when needed; no bundled personal documents are required.

## Project structure

```
frontend/                          Next.js UI
backend/PersonalAiAssistant.Api/
  ├── Program.cs                   API and endpoints
  ├── Services/                    Chat and embedding services
  └── Data/                        Database and vector storage
```

## What I'd do next

- Chunk on structure instead of length, and keep page or section metadata so answers can cite their source
- Stream responses so it doesn't look frozen
- More file formats beyond PDF, plain text and Markdown
- Some way to see how confident a retrieval was, so the assistant can say "I don't think this is in your documents" instead of guessing

## Built with

Next.js, ASP.NET Core, Ollama (llama3.1:8b, nomic-embed-text), PostgreSQL, pgvector
