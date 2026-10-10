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
});
describe("media player initialization", () => {
  it("keeps a supplied initial index instead of resetting it after initialization", async () => {
    const entries = [entry("/a.ass"), entry("/movie.mkv")];
    render(<MediaPlayer entries={entries} defaultActiveIndex={1} />);
    await waitFor(() => expect(screen.getByText("/movie.mkv")).toBeInTheDocument());
    expect(screen.queryByText("/a.ass")).not.toBeInTheDocument();
  });
  it("leaves normal input arrow keys to the input and only navigates when the player is focused", () => {
    render(<MediaPlayer entries={[entry("/a.mp4"), entry("/b.mp4")]} />);
    fireEvent.keyDown(screen.getByRole("textbox"), { key: "ArrowRight" });
    expect(screen.getByText("/a.mp4")).toBeInTheDocument();
    fireEvent.keyDown(screen.getByRole("region"), { key: "ArrowRight" });
    expect(screen.getByText("/b.mp4")).toBeInTheDocument();
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
