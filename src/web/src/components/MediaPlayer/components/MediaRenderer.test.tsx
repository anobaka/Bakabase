import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import MediaRenderer, { MEDIA_LOAD_TIMEOUT } from "./MediaRenderer";
import { IwFsType, MediaType } from "@/sdk/constants";
vi.mock("@/components/bakaui", () => ({ Spinner: () => <span data-testid="spinner" /> }));
vi.mock("@/components/TextReader", () => ({ default: () => <div>text-preview</div> }));
vi.mock("react-i18next", () => {
  const t = (key: string) => key;
  return { useTranslation: () => ({ t }) };
});
const entry = (path = "/film/main.mkv") => ({
  path,
  playPath: path,
  name: path.split("/").pop()!,
  type: IwFsType.Unknown,
  passwordsForDecompressing: [],
});
const props = {
  entry: entry(),
  mediaType: MediaType.Video,
  playing: false,
  currentInitialized: false,
  onLoad: vi.fn(),
};
afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.restoreAllMocks();
});
describe("browser media delivery", () => {
  it("attaches /file/play as an actual native video source instead of obsolete ReactPlayer url props", () => {
    const { container } = render(<MediaRenderer {...props} />);
    expect(container.querySelector("video")).toHaveAttribute(
      "src",
      "/file/play?fullname=%2Ffilm%2Fmain.mkv",
    );
    expect(container.querySelector("video")).toHaveAttribute("controls");
    fireEvent.canPlay(container.querySelector("video")!);
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
    expect(props.onLoad).toHaveBeenCalled();
  });
  it("allows a valid paused video to become ready on metadata even when autoplay is not permitted", () => {
    vi.useFakeTimers();
    const { container } = render(<MediaRenderer {...props} />);
    fireEvent.loadedMetadata(container.querySelector("video")!);
    act(() => vi.advanceTimersByTime(MEDIA_LOAD_TIMEOUT + 1));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(container.querySelector("video")).toBeInTheDocument();
  });
  it("replaces failed or timed-out media with an actionable terminal state and retries with a fresh element", () => {
    vi.useFakeTimers();
    const { container } = render(<MediaRenderer {...props} />);
    act(() => vi.advanceTimersByTime(MEDIA_LOAD_TIMEOUT));
    expect(screen.getByRole("alert")).toHaveTextContent("mediaPlayer.media.timeout");
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
    expect(container.querySelector("video")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "mediaPlayer.retry" }));
    expect(container.querySelector("video")).toBeInTheDocument();
    fireEvent.error(container.querySelector("video")!);
    expect(screen.getByRole("alert")).toHaveTextContent("mediaPlayer.media.failed");
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
  });
  it("never overlays unsupported files or text previews with an unrelated loading spinner", () => {
    const { rerender } = render(<MediaRenderer {...props} mediaType={MediaType.Unknown} />);
    expect(screen.getByText("mediaPlayer.unsupported")).toBeInTheDocument();
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
    rerender(<MediaRenderer {...props} entry={entry("/film/a.ass")} mediaType={MediaType.Text} />);
    expect(screen.getByText("text-preview")).toBeInTheDocument();
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
  });
  it("ignores the previous file's interrupted play promise after switching to a new renderer", async () => {
    let rejectOld!: (error: DOMException) => void;
    const pending = new Promise<void>((_resolve, reject) => {
      rejectOld = reject;
    });
    vi.spyOn(HTMLMediaElement.prototype, "play")
      .mockImplementationOnce(() => pending)
      .mockResolvedValue(undefined);
    const paused = vi.fn();
    const played = vi.fn();
    const { rerender, container } = render(
      <MediaRenderer key="first" {...props} playing onVideoPause={paused} onVideoPlay={played} />,
    );
    rerender(
      <MediaRenderer
        key="second"
        {...props}
        entry={entry("/film/next.mp4")}
        playing
        onVideoPause={paused}
        onVideoPlay={played}
      />,
    );
    await act(async () => rejectOld(new DOMException("interrupted by load", "AbortError")));
    expect(paused).not.toHaveBeenCalled();
    fireEvent.play(container.querySelector("video")!);
    expect(played).toHaveBeenCalledOnce();
    fireEvent.pause(container.querySelector("video")!);
    expect(paused).toHaveBeenCalledOnce();
  });
  it("does not mistake an intentionally paused metadata-only stream stall for failed playback", () => {
    vi.useFakeTimers();
    const { container } = render(<MediaRenderer {...props} />);
    const video = container.querySelector("video")!;
    Object.defineProperty(video, "readyState", { value: 1 });
    fireEvent.loadedMetadata(video);
    fireEvent.stalled(video);
    act(() => vi.advanceTimersByTime(MEDIA_LOAD_TIMEOUT));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
  });
});
