import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlayOnThisDevice from "..";

const mocks = vi.hoisted(() => ({
  browser: vi.fn(),
  native: vi.fn(),
  destroy: vi.fn(),
  danger: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    file: { getRawFileUrl: () => "/file/raw" },
    resource: { markResourceAsPlayed: vi.fn() },
  },
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div role="dialog">{children}</div>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Checkbox: ({ children, onValueChange, isSelected }: any) => (
    <label>
      <input
        checked={isSelected}
        type="checkbox"
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Chip: ({ children }: any) => <span>{children}</span>,
  Select: () => null,
  toast: { success: vi.fn(), danger: mocks.danger },
}));

const show = (desktop = false) =>
  render(
    <PlayOnThisDevice
      filePath="/album/001.jpg"
      resource={{ id: 42 } as any}
      onDestroyed={mocks.destroy}
      onPlayInBrowser={mocks.browser}
      onPlayNative={desktop ? mocks.native : undefined}
    />,
  );

beforeEach(() => {
  vi.clearAllMocks();
});
afterEach(cleanup);

describe("one-time opening alternatives", () => {
  it("explains an unavailable configured player and allows a one-time alternative", () => {
    render(
      <PlayOnThisDevice
        filePath="/album/001.jpg"
        resource={{ id: 42 } as any}
        unavailableReason="The configured player is unavailable."
        onPlayInBrowser={mocks.browser}
      />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("configured player is unavailable");
    fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser"));
    expect(mocks.browser).toHaveBeenCalledOnce();
  });
  it.each(["/album/cover.jpg", "/album/movie.mkv"])(
    "shows configured desktop playback and video selection for mixed resources without first-file URI handlers or links: %s",
    (filePath) => {
      const videos = vi.fn();

      render(
        <PlayOnThisDevice
          isMixed
          filePath={filePath}
          resource={{ id: 42 } as any}
          onPlayInBrowser={mocks.browser}
          onPlayNative={mocks.native}
          onPlayVideos={videos}
        />,
      );
      expect(screen.queryByText("PotPlayer")).toBeNull();
      expect(screen.getByText("resource.playOnThisDevice.desktopPlayer")).toBeInTheDocument();
      expect(screen.queryByRole("textbox")).toBeNull();
      fireEvent.click(screen.getByText("resource.playback.playVideos"));
      expect(videos).toHaveBeenCalledOnce();
      expect(mocks.native).not.toHaveBeenCalled();
      expect(mocks.browser).not.toHaveBeenCalled();
    },
  );

  it("uses the mixed desktop configured-player action without guessing an AV URI handler", async () => {
    render(
      <PlayOnThisDevice
        isMixed
        filePath="/album/movie.mkv"
        resource={{ id: 42 } as any}
        onDestroyed={mocks.destroy}
        onPlayInBrowser={mocks.browser}
        onPlayNative={mocks.native}
      />,
    );
    expect(screen.queryByText("PotPlayer")).toBeNull();
    expect(screen.queryByRole("textbox")).toBeNull();
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.desktopPlayer")),
    );
    expect(mocks.native).toHaveBeenCalledOnce();
    expect(mocks.destroy).toHaveBeenCalledOnce();
    expect(mocks.browser).not.toHaveBeenCalled();
  });

  it.each(["/album/cover.jpg", "/album/movie.mkv"])(
    "keeps mixed browser collections free of configured-player and first-file AV URI actions: %s",
    (filePath) => {
      render(
        <PlayOnThisDevice
          isMixed
          filePath={filePath}
          resource={{ id: 42 } as any}
          onPlayInBrowser={mocks.browser}
        />,
      );
      expect(screen.queryByText("resource.playOnThisDevice.desktopPlayer")).toBeNull();
      expect(screen.queryByText("PotPlayer")).toBeNull();
      expect(screen.queryByText("resource.playOnThisDevice.nativePlayers")).toBeNull();
      expect(screen.queryByRole("textbox")).toBeNull();
    },
  );
  it("uses a one-time browser choice", () => {
    show();
    fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser"));
    expect(mocks.browser).toHaveBeenCalledOnce();
  });

  it("preserves the desktop configured-player action without exposing AV URI handlers for an image", async () => {
    show(true);
    expect(screen.queryByText("PotPlayer")).toBeNull();
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.desktopPlayer")),
    );
    expect(mocks.native).toHaveBeenCalledOnce();
    expect(mocks.browser).not.toHaveBeenCalled();
  });

  it("does not expose per-kind remembering, reset or ask-every-time settings", () => {
    show(true);
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument();
    expect(screen.queryByText("resource.playback.askEveryTime")).not.toBeInTheDocument();
    expect(screen.queryByText("resource.playback.resetDefault")).not.toBeInTheDocument();
    expect(screen.queryByText("resource.playOnThisDevice.rememberChoice")).not.toBeInTheDocument();
  });
});
