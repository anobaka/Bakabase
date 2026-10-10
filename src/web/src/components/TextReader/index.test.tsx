import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import TextReader from "./index";
vi.mock("@/components/bakaui", () => ({ Spinner: () => <span>spinner</span> }));
vi.mock("react-i18next", () => {
  const t = (key: string) => key;
  return { useTranslation: () => ({ t }) };
});
beforeEach(() => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe() {}
      disconnect() {}
    },
  );
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});
describe("text reader UI", () => {
  it("renders NFO and subtitle syntax as escaped text and virtualizes long previews", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(
            new TextEncoder().encode(
              "<script>literal</script>\n  indented\n" +
                Array.from({ length: 10000 }, (_, i) => `line ${i}`).join("\n"),
            ),
          ),
        ),
    );
    const { container } = render(<TextReader file="/info.nfo" />);
    await screen.findByText("<script>literal</script>");
    expect(container.querySelector("script")).toBeNull();
    expect(screen.getByText("indented").textContent).toBe("  indented");
    expect(container.querySelectorAll(".text-reader-line").length).toBeLessThan(100);
    expect(screen.queryByText("line 9999")).not.toBeInTheDocument();
  });
  it("loads the newly selected file and cancels stale reads rather than retaining the first text", async () => {
    const fetch = vi
      .fn()
      .mockResolvedValueOnce(new Response("first file"))
      .mockResolvedValueOnce(new Response("second file"));
    vi.stubGlobal("fetch", fetch);
    const { rerender } = render(<TextReader file="/first.ass" />);
    await screen.findByText("first file");
    const firstSignal = fetch.mock.calls[0][1].signal;
    rerender(<TextReader file="/second.ass" />);
    await screen.findByText("second file");
    expect(firstSignal.aborted).toBe(true);
    expect(screen.queryByText("first file")).not.toBeInTheDocument();
    expect(fetch.mock.calls[1][0]).toContain("second.ass");
  });
  it("keeps virtualization bounded if an unconstrained ancestor reports the entire file height", async () => {
    vi.spyOn(HTMLDivElement.prototype, "clientHeight", "get").mockReturnValue(226182);
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(private callback: () => void) {}
        observe() {
          this.callback();
        }
        disconnect() {}
      },
    );
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(Array.from({ length: 12000 }, (_, index) => `row ${index}`).join("\n")),
        ),
    );
    const { container } = render(<TextReader file="/large.nfo" />);
    await screen.findByText("row 0");
    await waitFor(() =>
      expect(container.querySelectorAll(".text-reader-line").length).toBeLessThan(100),
    );
    expect(screen.queryByText("row 11999")).not.toBeInTheDocument();
  });
});
