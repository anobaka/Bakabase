import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";

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
  it("wraps by default and lets the user disable and reenable it without reloading the preview", async () => {
    const fetch = vi.fn().mockResolvedValue(new Response("a long source line"));

    vi.stubGlobal("fetch", fetch);
    const { container } = render(<TextReader file="/info.nfo" />);

    await screen.findByText("a long source line");
    const toggle = screen.getByRole("checkbox", { name: "mediaPlayer.text.wrap" });

    expect(toggle).toBeChecked();
    expect(container.querySelector(".text-reader-viewport")).toHaveClass(
      "text-reader-viewport-wrap",
    );
    expect(container.querySelector(".text-reader-lines")).toHaveStyle({ minWidth: "0" });
    fireEvent.click(toggle);
    expect(toggle).not.toBeChecked();
    expect(container.querySelector(".text-reader-viewport")).not.toHaveClass(
      "text-reader-viewport-wrap",
    );
    expect(
      parseFloat((container.querySelector(".text-reader-lines") as HTMLElement).style.minWidth),
    ).toBeGreaterThan(0);
    fireEvent.click(toggle);
    expect(toggle).toBeChecked();
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it("positions rows using measured wrapped heights and remeasures after the viewport width changes", async () => {
    let width = 200;

    vi.spyOn(HTMLDivElement.prototype, "clientWidth", "get").mockImplementation(() => width);
    vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (
      this: HTMLElement,
    ) {
      const wrap = this.closest(".text-reader-viewport-wrap");
      const height = this.classList.contains("text-reader-line")
        ? this.dataset.line === "0" && wrap
          ? width === 200
            ? 220
            : 110
          : 22
        : 0;

      return {
        width,
        height,
        top: 0,
        left: 0,
        bottom: height,
        right: width,
        x: 0,
        y: 0,
        toJSON() {},
      };
    });
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response(
            "x".repeat(1000) +
              "\n" +
              Array.from({ length: 10000 }, (_, index) => `row ${index}`).join("\n"),
          ),
        ),
    );
    const { container } = render(<TextReader file="/large.nfo" />);

    await screen.findByText("row 0");
    await waitFor(() =>
      expect(container.querySelector('[data-line="1"]')).toHaveStyle({ top: "220px" }),
    );
    expect(container.querySelectorAll(".text-reader-line").length).toBeLessThan(100);
    width = 400;
    fireEvent(window, new Event("resize"));
    await waitFor(() =>
      expect(container.querySelector('[data-line="1"]')).toHaveStyle({ top: "110px" }),
    );
    fireEvent.click(screen.getByRole("checkbox", { name: "mediaPlayer.text.wrap" }));
    await waitFor(() =>
      expect(container.querySelector('[data-line="1"]')).toHaveStyle({ top: "22px" }),
    );
    const viewport = screen.getByRole("region", { name: "mediaPlayer.text.preview" });

    fireEvent.scroll(viewport, { target: { scrollTop: 22 * 301 } });
    await screen.findByText("row 300");
    fireEvent.click(screen.getByRole("checkbox", { name: "mediaPlayer.text.wrap" }));
    await waitFor(() => expect(screen.getByText("row 300")).toBeInTheDocument());
    expect(viewport.scrollTop).toBeGreaterThan(22 * 301);
    expect(container.querySelectorAll(".text-reader-line").length).toBeLessThan(100);
    expect(screen.queryByText("row 9999")).not.toBeInTheDocument();
  });

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
