"use client";

import React, { useEffect, useState } from "react";

type DocumentMeta = {
  id: string;
  originalFileName: string;
  storedFileName: string;
  sizeBytes: number;
  uploadedAtUtc: string;
};

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  const kb = bytes / 1024;
  if (kb < 1024) return `${kb.toFixed(1)} KB`;
  const mb = kb / 1024;
  if (mb < 1024) return `${mb.toFixed(1)} MB`;
  const gb = mb / 1024;
  return `${gb.toFixed(1)} GB`;
}

export default function DocumentsPage() {
  const [docs, setDocs] = useState<DocumentMeta[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  async function load() {
    setLoading(true);
    setError(null);
    try {
      const res = await fetch("/api/documents");
      if (!res.ok) throw new Error();
      const data = (await res.json()) as DocumentMeta[];
      setDocs(data);
    } catch {
      setError("Could not load documents.");
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void load();
  }, []);

  return (
    <main className="min-h-screen bg-gray-50">
      <div className="mx-auto max-w-4xl px-4 py-6">
        <header className="mb-4 flex items-center justify-between gap-3">
          <div>
            <h1 className="text-2xl font-semibold">Documents</h1>
            <p className="text-sm text-gray-600">Uploaded files</p>
          </div>
          <div className="flex items-center gap-2">
            <a
              href="/"
              className="rounded-lg border bg-white px-3 py-2 text-sm shadow-sm"
            >
              Back to chat
            </a>
            <button
              className="rounded-lg bg-black px-3 py-2 text-sm font-medium text-white disabled:opacity-50"
              onClick={() => void load()}
              disabled={loading}
            >
              Refresh
            </button>
          </div>
        </header>

        <div className="rounded-xl border bg-white p-4 shadow-sm">
          {loading ? (
            <p className="text-sm text-gray-600">Loading…</p>
          ) : error ? (
            <p className="text-sm text-red-600">{error}</p>
          ) : docs.length === 0 ? (
            <p className="text-sm text-gray-600">
              No documents yet. Upload one from the chat page.
            </p>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b text-gray-600">
                  <tr>
                    <th className="py-2 pr-3">File</th>
                    <th className="py-2 pr-3">Size</th>
                    <th className="py-2 pr-3">Uploaded (UTC)</th>
                    <th className="py-2 pr-3">Id</th>
                  </tr>
                </thead>
                <tbody>
                  {docs.map((d) => (
                    <tr key={d.id} className="border-b last:border-b-0">
                      <td className="py-2 pr-3">{d.originalFileName}</td>
                      <td className="py-2 pr-3">{formatBytes(d.sizeBytes)}</td>
                      <td className="py-2 pr-3">
                        {new Date(d.uploadedAtUtc)
                          .toISOString()
                          .replace("T", " ")
                          .slice(0, 19)}
                      </td>
                      <td className="py-2 pr-3 font-mono text-xs text-gray-600">
                        {d.id}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
