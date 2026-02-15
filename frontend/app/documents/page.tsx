"use client";

import React, { useEffect, useState } from "react";

type Doc = {
  id: string;
  originalFileName: string;
  storedFileName: string;
  sizeBytes: number;
  uploadedAtUtc: string;
};

type IngestState = "idle" | "ingesting" | "done" | "error";

export default function DocumentsPage() {
  const [docs, setDocs] = useState<Doc[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [ingestById, setIngestById] = useState<Record<string, IngestState>>({});

  async function loadDocs() {
    setLoading(true);
    setError(null);

    try {
      const res = await fetch("/api/documents");
      if (!res.ok) throw new Error(`Failed to load: ${res.status}`);

      const data = (await res.json()) as Doc[];
      setDocs(data);
    } catch (e) {
      setError("Could not load documents.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadDocs();
  }, []);

  async function ingestDoc(id: string) {
    setIngestById((prev) => ({ ...prev, [id]: "ingesting" }));

    try {
      const res = await fetch(`/api/documents/${id}/ingest`, {
        method: "POST",
      });

      if (!res.ok) throw new Error(`Ingest failed: ${res.status}`);

      setIngestById((prev) => ({ ...prev, [id]: "done" }));
    } catch {
      setIngestById((prev) => ({ ...prev, [id]: "error" }));
    }
  }

  return (
    <main className="min-h-screen bg-gray-50">
      <div className="mx-auto max-w-3xl px-4 py-6">
        <header className="mb-4 flex items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">Documents</h1>
            <p className="text-sm text-gray-600">
              Upload, download, and ingest documents
            </p>
          </div>

          <a href="/" className="text-sm underline text-gray-700">
            Back to chat
          </a>
        </header>

        <div className="rounded-xl border bg-white p-4 shadow-sm">
          <div className="mb-3 flex items-center justify-between">
            <div className="text-sm font-medium text-gray-800">
              Your documents
            </div>
            <button
              className="rounded-lg border px-3 py-1.5 text-sm hover:bg-gray-50 disabled:opacity-50"
              onClick={() => void loadDocs()}
              disabled={loading}
            >
              Refresh
            </button>
          </div>

          {loading ? (
            <div className="text-sm text-gray-600">Loading…</div>
          ) : error ? (
            <div className="text-sm text-red-600">{error}</div>
          ) : docs.length === 0 ? (
            <div className="text-sm text-gray-600">No documents yet.</div>
          ) : (
            <div className="space-y-3">
              {docs.map((d) => {
                const state = ingestById[d.id] ?? "idle";

                return (
                  <div
                    key={d.id}
                    className="flex flex-col gap-2 rounded-xl border p-3"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="font-medium text-gray-900">
                          {d.originalFileName}
                        </div>
                        <div className="text-xs text-gray-500">
                          id: {d.id}
                        </div>
                      </div>

                      <div className="flex items-center gap-2">
                        <a
                          className="rounded-lg border px-3 py-1.5 text-sm hover:bg-gray-50"
                          href={`/api/documents/${d.id}/download`}
                        >
                          Download
                        </a>

                        <button
                          className="rounded-lg bg-black px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
                          onClick={() => void ingestDoc(d.id)}
                          disabled={state === "ingesting"}
                        >
                          {state === "ingesting" ? "Ingesting…" : "Ingest"}
                        </button>
                      </div>
                    </div>

                    {state === "done" ? (
                      <div className="text-sm text-green-700">
                        Ingest complete ✅
                      </div>
                    ) : state === "error" ? (
                      <div className="text-sm text-red-600">
                        Ingest failed ❌ (check backend logs)
                      </div>
                    ) : (
                      <div className="text-xs text-gray-500">
                        Ingest creates chunks + embeddings for search/chat.
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
