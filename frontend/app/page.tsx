"use client";

import React, { useEffect, useRef, useState } from "react";

type Role = "user" | "assistant";

type ChatMessage = {
  id: string;
  role: Role;
  content: string;
  createdAt: number;
};

function uid() {
  return crypto.randomUUID();
}

export default function HomePage() {
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: uid(),
      role: "assistant",
      content: "Hey! I’m your Personal AI Knowledge Assistant. Ask me anything.",
      createdAt: Date.now(),
    },
  ]);

  const [input, setInput] = useState("");
  const [isSending, setIsSending] = useState(false);
  const [isUploading, setIsUploading] = useState(false);

  const listRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    listRef.current?.scrollTo({
      top: listRef.current.scrollHeight,
      behavior: "smooth",
    });
  }, [messages]);

  async function uploadFile(file: File) {
    setIsUploading(true);

    try {
      const form = new FormData();
      form.append("file", file);

      const res = await fetch("/api/documents", {
        method: "POST",
        body: form,
      });

      if (!res.ok) throw new Error(`Upload failed: ${res.status}`);

      const data = (await res.json()) as {
        id: string;
        originalFileName: string;
        storedFileName: string;
        sizeBytes: number;
      };

      setMessages((prev) => [
        ...prev,
        {
          id: uid(),
          role: "assistant",
          content: `Uploaded: ${data.originalFileName}`,
          createdAt: Date.now(),
        },
      ]);
    } catch {
      setMessages((prev) => [
        ...prev,
        {
          id: uid(),
          role: "assistant",
          content: "Upload failed. Please try again.",
          createdAt: Date.now(),
        },
      ]);
    } finally {
      setIsUploading(false);
    }
  }

  async function sendMessage() {
    const text = input.trim();
    if (!text || isSending) return;

    setIsSending(true);
    setInput("");

    const userMsg: ChatMessage = {
      id: uid(),
      role: "user",
      content: text,
      createdAt: Date.now(),
    };

    const thinkingId = uid();

    setMessages((prev) => [
      ...prev,
      userMsg,
      {
        id: thinkingId,
        role: "assistant",
        content: "Thinking…",
        createdAt: Date.now(),
      },
    ]);

    try {
      const res = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ message: text }),
      });

      if (!res.ok) throw new Error(`Request failed: ${res.status}`);

      const data = (await res.json()) as { reply: string };

      setMessages((prev) =>
        prev.map((m) =>
          m.id === thinkingId ? { ...m, content: data.reply } : m
        )
      );
    } catch {
      setMessages((prev) =>
        prev.map((m) =>
          m.id === thinkingId
            ? { ...m, content: "Something went wrong. Please try again." }
            : m
        )
      );
    } finally {
      setIsSending(false);
    }
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      void sendMessage();
    }
  }

  return (
    <main className="min-h-screen bg-gray-50">
      <div className="mx-auto flex min-h-screen max-w-3xl flex-col px-4 py-6">
        <header className="mb-4">
          <div className="flex items-start justify-between gap-4">
            <div>
              <h1 className="text-2xl font-semibold">Personal AI Assistant</h1>
              <p className="text-sm text-gray-600">Chat UI</p>
            </div>

            <a href="/documents" className="text-sm underline text-gray-700">
              Documents
            </a>
          </div>

          <div className="mt-3 flex items-center gap-3">
            <label className="inline-flex cursor-pointer items-center rounded-lg bg-black px-4 py-2 text-sm font-medium text-white">
              <input
                type="file"
                className="hidden"
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) void uploadFile(f);
                  e.currentTarget.value = "";
                }}
                disabled={isUploading}
              />
              {isUploading ? "Uploading..." : "Upload document"}
            </label>
            <span className="text-xs text-gray-500">PDF or text</span>
          </div>
        </header>

        <div
          ref={listRef}
          className="flex-1 space-y-3 overflow-y-auto rounded-xl border bg-white p-4 shadow-sm"
        >
          {messages.map((m) => (
            <div
              key={m.id}
              className={`flex ${
                m.role === "user" ? "justify-end" : "justify-start"
              }`}
            >
              <div
                className={`max-w-[85%] whitespace-pre-wrap rounded-2xl px-4 py-2 text-sm leading-relaxed ${
                  m.role === "user"
                    ? "bg-black text-white"
                    : "bg-gray-100 text-gray-900"
                }`}
              >
                {m.content}
              </div>
            </div>
          ))}
        </div>

        <div className="mt-4 rounded-xl border bg-white p-3 shadow-sm">
          <div className="flex items-end gap-3">
            <textarea
              className="min-h-[48px] flex-1 resize-none rounded-lg border px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-black/20 disabled:bg-gray-50"
              placeholder="Type your message…"
              value={input}
              onChange={(e) => setInput(e.target.value)}
              onKeyDown={onKeyDown}
              disabled={isSending}
            />
            <button
              className="rounded-lg bg-black px-4 py-2 text-sm font-medium text-white disabled:opacity-50"
              onClick={() => void sendMessage()}
              disabled={isSending || !input.trim()}
            >
              Send
            </button>
          </div>
        </div>
      </div>
    </main>
  );
}
