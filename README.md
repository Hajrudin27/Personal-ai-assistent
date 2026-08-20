# Personal AI Assistant (Local RAG)

Upload your own documents and ask questions about them, with everything running on your own machine. Next.js frontend, ASP.NET Core backend, Ollama for the models, PostgreSQL with pgvector for the search.

## Why I built it

I kept reading about retrieval-augmented generation and realised I could describe roughly what it did but couldn't have built one. That usually means I don't actually understand it. So I built one.

I also wanted it to run locally rather than against OpenAI's API, for two reasons. The obvious one is that the documents you most want a searchable assistant for are usually the ones you'd rather not upload somewhere — notes, contracts, anything with other people's information in it. The less obvious one is that when a system is free to run, you experiment with it. If every test query costs money, you stop poking at it, and poking at it is how you find out what it's actually doing.

## What it does

Upload a PDF or text file, hit ingest, and the document gets split up, embedded and stored. After that the chat answers using what's in your documents rather than whatever the model happened to memorise during training.

It's all on your own machine. Nothing leaves it, and it works with the wifi off.

## Why I wrote the pipeline myself

Most RAG projects are Python with LangChain, where the whole thing is about twenty lines because the library does the work. I wrote the ingestion and retrieval in C# instead, without a framework.

That was slower and the code is less impressive to look at. But it meant I had to make each decision myself rather than accept a default: how to split the text, how many chunks to retrieve, what to do when nothing matched well. Those decisions are the actual substance of RAG, and using a library would have hidden every one of them from me.

## How it works

```
document  ->  split into chunks
          ->  each chunk through the embedding model (nomic-embed-text)
          ->  vectors stored in PostgreSQL with pgvector

question  ->  embedded with the same model
          ->  nearest chunks found by vector similarity
          ->  those chunks passed to the chat model (llama3) as context
          ->  answer
```

The part that surprised me is how much the answer quality depends on the retrieval step rather than on the model. A good model given the wrong three paragraphs will confidently answer the wrong question. Most of the time I spent tuning this went into retrieval, not into prompting.

One thing worth knowing if you build something similar: the embedding model and the stored vectors are tied together. `nomic-embed-text` produces vectors of a fixed size, and the pgvector column is declared with that size. Swap the embedding model and every vector already in the database is meaningless, because the new model puts things in a different space entirely. There's no error, just quietly worse results. Changing the model means re-ingesting everything.

## What's still rough

Being honest about where it is:

- **Chunking is naive.** It splits on length rather than on meaning, so a chunk can end mid-sentence or split a table down the middle. Chunking on paragraph or heading boundaries would help a lot, and it's the first thing I'd fix.
- **No metadata on chunks.** I store the text and the vector, but not which page or section it came from, so the assistant can't tell you where an answer came from. For anything you'd actually rely on, that citation matters more than the answer.
- **Responses aren't streamed.** You wait for the whole answer, which on a local model is long enough to feel broken.
- **Single user.** No accounts, no separation between one person's documents and another's.

## Running it

You'll need Node.js, the .NET 8 SDK, PostgreSQL and [Ollama](https://ollama.com).

**Models:**

```bash
ollama pull llama3
ollama pull nomic-embed-text
```

**Database:**

```sql
CREATE DATABASE personal_ai;
CREATE EXTENSION vector;
```

Then point the connection string at it in `backend/PersonalAiAssistant.Api/appsettings.json`.

**Backend** (starts on `http://localhost:5146`):

```bash
cd backend/PersonalAiAssistant.Api
dotnet run
```

**Frontend** (starts on `http://localhost:3000`):

```bash
cd frontend
npm install
npm run dev
```

Open the Documents page, upload a file, click Ingest, then go and ask it something.

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
- More file formats than PDF and plain text
- Some way to see how confident a retrieval was, so the assistant can say "I don't think this is in your documents" instead of guessing

## Built with

Next.js, ASP.NET Core, Ollama (llama3, nomic-embed-text), PostgreSQL, pgvector
