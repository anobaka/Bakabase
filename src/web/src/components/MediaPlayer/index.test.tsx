import React from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import MediaPlayer from "./index";

import { IwFsType } from "@/sdk/constants";
const mocks = vi.hoisted(() => ({ files: vi.fn(), archive: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({
  default: { file: { getAllFiles: mocks.files, getCompressedFileEntries: mocks.archive } },
}));
vi.mock("@/components/bakaui", () => ({ Spinner: () => <span>spinner</span> }));
vi.mock("./components/MediaPlayerLayout", () => ({
  default: (props: any) => (
    <div>
      <span>{props.activeEntry.path}</span>
      {props.renderFooterExtra?.(props.activeEntry.playPath || props.activeEntry.path)}
      <input aria-label="filter" />
      <button onClick={props.onNextEntry}>Next</button>
    </div>
  ),
}));
const entry = (path: string, type = IwFsType.Unknown) => ({
  path,
  name: path,
  type,
  passwordsForDecompressing: [],
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
  vi.restoreAllMocks();
});
describe("media player initialization", () => {
  it("passes footer context to each selected file without forwarding the callback to the DOM", () => {
    const entries = [entry("/a.jpg"), entry("/b.jpg")];

    const { container } = render(
      <MediaPlayer
        defaultActiveIndex={0}
        entries={entries}
        renderFooterExtra={(path) => <span data-testid="footer-context">{path}</span>}
      />,
    );

    expect(screen.getByTestId("footer-context")).toHaveTextContent("/a.jpg");
    fireEvent.click(screen.getByText("Next"));
    expect(screen.getByTestId("footer-context")).toHaveTextContent("/b.jpg");
    expect(container.firstChild).not.toHaveAttribute("renderFooterExtra");
  });
  it("keeps a supplied initial index instead of resetting it after initialization", async () => {
    const entries = [entry("/a.ass"), entry("/movie.mkv")];

    render(<MediaPlayer defaultActiveIndex={1} entries={entries} />);
    await waitFor(() => expect(screen.getByText("/movie.mkv")).toBeInTheDocument());
    expect(screen.queryByText("/a.ass")).not.toBeInTheDocument();
  });
  it("keeps whole-directory browsing until the user explicitly chooses configured candidates", () => {
    render(
      <MediaPlayer
        resourceScope
        configuredPaths={["/main.mkv"]}
        defaultActiveIndex={0}
        entries={[entry("/cover.jpg"), entry("/main.mkv"), entry("/extra.mp4")]}
      />,
    );
    expect(screen.getByText("/cover.jpg")).toBeInTheDocument();
    fireEvent.change(screen.getByRole("combobox", { name: "mediaPlayer.scope.label" }), {
      target: { value: "configured" },
    });
    expect(screen.getByText("/main.mkv")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Next"));
    expect(screen.getByText("/main.mkv")).toBeInTheDocument();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "resource" } });
    expect(screen.getByText("/cover.jpg")).toBeInTheDocument();
    expect(mocks.files).not.toHaveBeenCalled();
  });
  it("matches Windows configured candidates without case changes affecting their scope", () => {
    render(
      <MediaPlayer
        resourceScope
        configuredPaths={["c:/media/MAIN.mkv"]}
        defaultActiveIndex={1}
        entries={[entry("C:\\Media\\cover.jpg"), entry("C:\\Media\\main.mkv")]}
        initialScope="configured"
      />,
    );
    expect(screen.getByText("C:\\Media\\main.mkv")).toBeInTheDocument();
  });
  it("lets an empty configured scope be switched back to all files", () => {
    render(
      <MediaPlayer
        resourceScope
        configuredPaths={[]}
        entries={[entry("/cover.jpg")]}
        initialScope="configured"
      />,
    );
    expect(screen.getByText("mediaPlayer.scope.configuredEmpty")).toBeInTheDocument();
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "resource" } });
    expect(screen.getByText("/cover.jpg")).toBeInTheDocument();
  });
  it("leaves normal input arrow keys to the input and only navigates when the player is focused", () => {
    render(<MediaPlayer entries={[entry("/a.mp4"), entry("/b.mp4")]} />);
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "ArrowRight" });
    expect(screen.getByText("/a.mp4")).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole("region"), { key: "ArrowRight" });
    expect(screen.getByText("/b.mp4")).toBeInTheDocument();
  });
  it("switches images with wheel and stops using wheel when the next entry is video", () => {
    let now = 1000;

    vi.spyOn(performance, "now").mockImplementation(() => now);
    render(
      <MediaPlayer
        defaultActiveIndex={0}
        entries={[entry("/a.jpg"), entry("/b.jpg"), entry("/c.mp4"), entry("/d.jpg")]}
      />,
    );
    fireEvent.wheel(screen.getByRole("region"), { deltaY: 100 });
    expect(screen.getByText("/b.jpg")).toBeInTheDocument();
    now += 300;
    fireEvent.wheel(screen.getByRole("region"), { deltaY: 100 });
    expect(screen.getByText("/c.mp4")).toBeInTheDocument();
    now += 300;
    fireEvent.wheel(screen.getByRole("region"), { deltaY: 100 });
    expect(screen.getByText("/c.mp4")).toBeInTheDocument();
  });
  it("bounds directory expansion and allows retry after the aborted request", async () => {
    vi.useFakeTimers();
    mocks.files.mockImplementation(
      (_options, request) =>
        new Promise((_resolve, reject) =>
          request.signal.addEventListener("abort", () =>
            reject(new DOMException("Aborted", "AbortError")),
          ),
        ),
    );
    render(<MediaPlayer entries={[entry("/folder", IwFsType.Directory)]} />);
    await act(async () => {
      vi.advanceTimersByTime(15_000);
      await Promise.resolve();
    });
    expect(screen.getByText("mediaPlayer.filesFailed")).toBeInTheDocument();
    mocks.files.mockResolvedValue({ code: 0, data: ["/folder/movie.mp4"] });
    await act(async () =>
      fireEvent.click(screen.getByRole("button", { name: "mediaPlayer.retry" })),
    );
    expect(screen.getByText("/folder/movie.mp4")).toBeInTheDocument();
  });
});
