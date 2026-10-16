import React from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";

import MediaRenderer, { MEDIA_LOAD_TIMEOUT } from "./MediaRenderer";

import { IwFsType, MediaType } from "@/sdk/constants";
vi.mock("@/components/Resource/components/PlayControl/DesktopPlaybackGuide", () => ({
  DesktopPlaybackGuideButton: () => <button>desktop playback guide</button>,
}));
vi.mock("@/components/bakaui", () => ({ Spinner: () => <span data-testid="spinner" /> }));
vi.mock("@/components/TextReader", () => ({
  default: ({ sourceUrl }: any) => <div data-source={sourceUrl}>text-preview</div>,
}));
vi.mock("react-i18next", () => {
  const t = (key: string) => key;

  return { useTranslation: () => ({ t }) };
});
const entry = (path = "/film/main.mkv") => ({
  path,
  playPath: path,
  name: path.split("/").pop()!,
  meaningfulName: path.split("/").pop()!,
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
  it.each([MediaType.Image, MediaType.Audio, MediaType.Video])(
    "uses the owner-bound media URL instead of a manager file path (%s)",
    (type) => {
      const url =
        "/federation/local/management/owner/media?libraryEpoch=epoch&resourceId=42&key=remote-file";
      const { container } = render(
        <MediaRenderer {...props} entry={{ ...entry(), mediaUrl: url }} mediaType={type} />,
      );

      expect(container.querySelector("img, audio, video")).toHaveAttribute("src", url);
    },
  );
  it("passes the owner-bound URL to text previews while retaining the logical filename", () => {
    const url =
      "/federation/local/management/owner/media?libraryEpoch=epoch&resourceId=42&key=remote-file";

    render(
      <MediaRenderer
        {...props}
        entry={{ ...entry("/remote/info.txt"), mediaUrl: url }}
        mediaType={MediaType.Text}
      />,
    );
    expect(screen.getByText("text-preview")).toHaveAttribute("data-source", url);
  });
  it("reports actual delivery results per item and keeps a failed item retryable", async () => {
    const state = vi.fn();
    const alternative = vi.fn();
    const { container } = render(
      <MediaRenderer {...props} onOpenAlternative={alternative} onPreviewStateChange={state} />,
    );

    expect(state).toHaveBeenLastCalledWith("/film/main.mkv", "loading");
    fireEvent.canPlay(container.querySelector("video")!);
    expect(state).toHaveBeenLastCalledWith("/film/main.mkv", "ready");
    fireEvent.error(container.querySelector("video")!);
    expect(state).toHaveBeenLastCalledWith("/film/main.mkv", "failed");
    await act(async () =>
      fireEvent.click(screen.getByText("mediaPlayer.preview.chooseAlternative")),
    );
    expect(alternative).toHaveBeenCalledWith("/film/main.mkv");
    fireEvent.click(screen.getByText("mediaPlayer.retry"));
    expect(state).toHaveBeenLastCalledWith("/film/main.mkv", "loading");
    expect(container.querySelector("video")).toBeInTheDocument();
  });
  it("does not promise a native action when the caller supplies no available alternative", () => {
    render(<MediaRenderer {...props} mediaType={MediaType.Unknown} />);
    expect(screen.queryByText("mediaPlayer.preview.chooseAlternative")).not.toBeInTheDocument();
  });
  it("offers an explicit alternative for non-preview files and reports an alternative failure", async () => {
    const alternative = vi.fn().mockRejectedValue(new Error("missing mapped file"));
    const state = vi.fn();

    render(
      <MediaRenderer
        {...props}
        entry={entry("/game/start.exe")}
        mediaType={MediaType.Unknown}
        onOpenAlternative={alternative}
        onPreviewStateChange={state}
      />,
    );
    expect(state).toHaveBeenLastCalledWith("/game/start.exe", "unavailable");
    await act(async () =>
      fireEvent.click(screen.getByText("mediaPlayer.preview.chooseAlternative")),
    );
    expect(screen.getByRole("alert")).toHaveTextContent("mediaPlayer.preview.alternativeFailed");
  });
  it("offers player compatibility guidance for decoding failures after receiving media metadata", () => {
    const { container } = render(<MediaRenderer {...props} />);
    const video = container.querySelector("video")!;

    fireEvent.loadedMetadata(video);
    Object.defineProperty(video, "error", { value: { code: 3 } });
    fireEvent.error(video);
    expect(screen.getByText("desktop playback guide")).toBeInTheDocument();
  });
  it.each([undefined, 1, 2, 3, 4])(
    "does not recommend installing for an initial failure before any metadata (%s)",
    (code) => {
      const { container } = render(<MediaRenderer {...props} />);
      const video = container.querySelector("video")!;

      // HTTP failures can surface as code 4 too; this event alone does not prove
      // that source bytes were accessible or that a desktop player would help.
      Object.defineProperty(video, "error", { value: { code } });
      fireEvent.error(video);
      expect(screen.queryByText("desktop playback guide")).not.toBeInTheDocument();
    },
  );
  it("does not interpret code 4 as confirmed format incompatibility even after metadata", () => {
    const { container } = render(<MediaRenderer {...props} />);
    const video = container.querySelector("video")!;

    fireEvent.loadedMetadata(video);
    Object.defineProperty(video, "error", { value: { code: 4 } });
    fireEvent.error(video);
    expect(screen.queryByText("desktop playback guide")).not.toBeInTheDocument();
  });
  it("forgets successful metadata on retry and when changing file or media type", () => {
    const { container, rerender } = render(<MediaRenderer {...props} />);
    const failDecoding = (element: HTMLMediaElement) => {
      Object.defineProperty(element, "error", { value: { code: 3 }, configurable: true });
      fireEvent.error(element);
    };

    fireEvent.loadedMetadata(container.querySelector("video")!);
    failDecoding(container.querySelector("video")!);
    expect(screen.getByText("desktop playback guide")).toBeInTheDocument();
    fireEvent.click(screen.getByText("mediaPlayer.retry"));
    failDecoding(container.querySelector("video")!);
    expect(screen.queryByText("desktop playback guide")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("mediaPlayer.retry"));
    fireEvent.loadedMetadata(container.querySelector("video")!);
    rerender(<MediaRenderer {...props} entry={entry("/film/next.mp4")} />);
    failDecoding(container.querySelector("video")!);
    expect(screen.queryByText("desktop playback guide")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("mediaPlayer.retry"));
    fireEvent.loadedMetadata(container.querySelector("video")!);
    rerender(
      <MediaRenderer {...props} entry={entry("/film/next.mp4")} mediaType={MediaType.Audio} />,
    );
    failDecoding(container.querySelector("audio")!);
    expect(screen.queryByText("desktop playback guide")).not.toBeInTheDocument();
  });
  it("offers guidance for a file format the browser cannot preview", () => {
    render(<MediaRenderer {...props} mediaType={MediaType.Unknown} />);
    expect(screen.getByText("desktop playback guide")).toBeInTheDocument();
  });
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
        playing
        entry={entry("/film/next.mp4")}
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
