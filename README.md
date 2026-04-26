📘 Personal AI Assistant (Local RAG)
A local-first Personal AI Assistant built with Next.js + ASP.NET Core + Ollama, supporting document ingestion and retrieval-augmented generation (RAG).
This project allows you to upload documents, ingest them into a vector database, and chat with an AI that answers based on your own data — completely locally, without external APIs.

✨ Features
✅ Local AI chat powered by Ollama
📄 Upload and ingest documents
🔍 Semantic search using embeddings
🧠 Retrieval-Augmented Generation (RAG)
🗄️ Vector storage with PostgreSQL + pgvector
🌐 Modern frontend built with Next.js
⚡ Backend API built with ASP.NET Core

🛠 Tech Stack
Layer	Technology
Frontend	Next.js (React)
Backend	    ASP.NET Core Web API
AI Model	Ollama (Local LLM)
Embeddings	Ollama embedding models (nomic-embed-text)
Database	PostgreSQL + pgvector
Document RAG	Custom ingestion + vector search

📂 Project Structure
Personal-ai-assistent/
│
├── frontend/                # Next.js UI
│
├── backend/
│   └── PersonalAiAssistant.Api/
│       ├── Program.cs       # Main API + endpoints
│       ├── Services/        # AI + embedding services
│       └── Data/            # Database + vector storage
│
└── README.md

🚀 Getting Started
Follow these steps to run the project locally.

1️⃣ Requirements
Make sure you have installed:
Node.js
.NET 8 SDK
PostgreSQL
Ollama

2️⃣ Install Ollama Models
Pull a chat model:
ollama pull llama3
Pull an embedding model:
ollama pull nomic-embed-text
3️⃣ Setup Database (PostgreSQL + pgvector)
Create a PostgreSQL database, for example:

CREATE DATABASE personal_ai;
Enable pgvector extension:
CREATE EXTENSION vector;
Update the backend connection string inside:
backend/PersonalAiAssistant.Api/appsettings.json
Example:
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=personal_ai;Username=postgres;Password=yourpassword"
}

4️⃣ Run Backend API
Go into the backend folder:
cd backend/PersonalAiAssistant.Api
Run the API:
dotnet run
Backend will start at:
http://localhost:5146
5️⃣ Run Frontend
Open a new terminal:
cd frontend
npm install
npm run dev
Frontend will start at:
http://localhost:3000
📄 Document Ingestion
Open the Documents page
Upload a file (PDF/Text)
Click Ingest
The document is embedded and stored in the vector database
Chat will now answer using your uploaded knowledge
🔍 How RAG Works (Simplified)
User uploads documents
Backend splits text into chunks
Each chunk is converted into embeddings
Embeddings are stored in pgvector
User asks a question
Most relevant chunks are retrieved
AI responds with context from your data

🎓 Context
This project was developed as part of a self learning project. 
Local AI systems
Vector databases
Retrieval-Augmented Generation
Full-stack AI integration

📌 Future Improvements
Better chunking and metadata
Streaming responses
Support for more file formats
UI improvements for ingestion progress
Multi-user support

📜 License
This project is for educational purposes.
