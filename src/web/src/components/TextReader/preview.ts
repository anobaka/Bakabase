export const MAX_PREVIEW_BYTES = 512 * 1024;
export const MAX_PREVIEW_LINES = 20_000;
export const MAX_LINE_LENGTH = 4096;

export type TextPreview = { text: string; truncated: boolean; bytes: number };

/** Streaming is mandatory: a server which ignores Range must not make us buffer
 * the entire file before applying a preview limit. */
export async function readTextPreview(
  url: string,
  signal: AbortSignal,
  encoding = "utf-8",
): Promise<TextPreview> {
  const response = await fetch(url, { signal, headers: { Range: `bytes=0-${MAX_PREVIEW_BYTES}` } });
  if (response.status === 416 && /^bytes \*\/0$/i.test(response.headers.get("content-range") || ""))
    return { text: "", truncated: false, bytes: 0 };
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  if (!response.body) {
    if (response.status === 204 || response.headers.get("content-length") === "0")
      return { text: "", truncated: false, bytes: 0 };
    throw new Error("Streaming unavailable");
  }
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let bytes = 0;
  let truncated = false;
  const total =
    /\/(\d+)$/.exec(response.headers.get("content-range") || "")?.[1] ||
    response.headers.get("content-length");
  if (total && Number(total) > MAX_PREVIEW_BYTES) truncated = true;
  try {
    while (bytes <= MAX_PREVIEW_BYTES) {
      const { value, done } = await reader.read();
      if (done) break;
      if (signal.aborted) throw new DOMException("Aborted", "AbortError");
      const remaining = MAX_PREVIEW_BYTES - bytes;
      if (value.length > remaining) {
        chunks.push(value.subarray(0, remaining));
        bytes += remaining;
        truncated = true;
        break;
      }
      chunks.push(value);
      bytes += value.length;
    }
  } finally {
    await reader.cancel().catch(() => {});
    reader.releaseLock();
  }
  const content = new Uint8Array(bytes);
  let offset = 0;
  for (const chunk of chunks) {
    content.set(chunk, offset);
    offset += chunk.length;
  }
  const detected =
    encoding === "utf-8" && content[0] === 0xff && content[1] === 0xfe
      ? "utf-16le"
      : encoding === "utf-8" && content[0] === 0xfe && content[1] === 0xff
        ? "utf-16be"
        : encoding;
  const decoded = new TextDecoder(detected).decode(content);
  const lines = decoded.replace(/\r\n?/g, "\n").split("\n");
  if (lines.length > MAX_PREVIEW_LINES) truncated = true;
  const limited = lines.slice(0, MAX_PREVIEW_LINES).map((line) => {
    if (line.length <= MAX_LINE_LENGTH) return line;
    truncated = true;
    return `${line.slice(0, MAX_LINE_LENGTH)}…`;
  });
  return { text: limited.join("\n"), truncated, bytes };
}
