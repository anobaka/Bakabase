import { afterEach, describe, expect, it, vi } from "vitest";
import { MAX_LINE_LENGTH, MAX_PREVIEW_BYTES, MAX_PREVIEW_LINES, readTextPreview } from "./preview";
afterEach(() => vi.unstubAllGlobals());
const signal = () => new AbortController().signal;
const response = (text: string, init?: ResponseInit) =>
  new Response(new TextEncoder().encode(text), init);
describe("bounded text preview", () => {
  it("requests a byte range and preserves subtitle syntax, NFO indentation and blank lines", async () => {
    const text =
      "  <script>literal text</script>\r\n\r\nDialogue: 0,0:01:00.00,0:01:05.00,Default,,0,0,0,,字幕";
    const fetch = vi.fn().mockResolvedValue(response(text));
    vi.stubGlobal("fetch", fetch);
    const preview = await readTextPreview("/file/raw?fullname=a.ass", signal());
    expect(fetch.mock.calls[0][1].headers.Range).toBe(`bytes=0-${MAX_PREVIEW_BYTES}`);
    expect(preview.text).toBe(text.replace(/\r\n/g, "\n"));
    expect(preview.truncated).toBe(false);
  });
  it("cancels an oversized stream when Range is ignored without calling full-body buffer helpers", async () => {
    const cancel = vi.fn();
    const body = new ReadableStream({
      start(controller) {
        controller.enqueue(new Uint8Array(MAX_PREVIEW_BYTES + 100).fill(65));
      },
      cancel,
    });
    const res = new Response(body, { status: 200 });
    const text = vi.spyOn(res, "text");
    const arrayBuffer = vi.spyOn(res, "arrayBuffer");
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(res));
    const preview = await readTextPreview("/file/raw", signal());
    expect(preview.bytes).toBe(MAX_PREVIEW_BYTES);
    expect(preview.truncated).toBe(true);
    expect(cancel).toHaveBeenCalled();
    expect(text).not.toHaveBeenCalled();
    expect(arrayBuffer).not.toHaveBeenCalled();
  });
  it("reads the total length from Content-Range and limits extreme lines and line counts", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          response("A".repeat(MAX_LINE_LENGTH + 10), {
            status: 206,
            headers: { "Content-Range": "bytes 0-4105/1000000" },
          }),
        ),
    );
    let preview = await readTextPreview("/file/raw", signal());
    expect(preview.truncated).toBe(true);
    expect(preview.text.length).toBe(MAX_LINE_LENGTH + 1);
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(response("x\n".repeat(MAX_PREVIEW_LINES + 1))),
    );
    preview = await readTextPreview("/file/raw", signal());
    expect(preview.text.split("\n")).toHaveLength(MAX_PREVIEW_LINES);
    expect(preview.truncated).toBe(true);
  });
  it("treats a confirmed empty range response as empty but rejects other HTTP errors", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(null, { status: 416, headers: { "Content-Range": "bytes */0" } }),
        ),
    );
    expect(await readTextPreview("/file/raw", signal())).toEqual({
      text: "",
      bytes: 0,
      truncated: false,
    });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("Denied", { status: 403 })));
    await expect(readTextPreview("/file/raw", signal())).rejects.toThrow("HTTP 403");
  });
});
