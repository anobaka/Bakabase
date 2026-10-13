import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlayOnThisDevice from "..";

const mocks = vi.hoisted(() => ({
  browser: vi.fn(),
  native: vi.fn(),
  save: vi.fn(),
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
      onRememberChoice={mocks.save}
    />,
  );

beforeEach(() => {
  vi.clearAllMocks();
  mocks.save.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("remembering the current device's opening method", () => {
  it.each(["/album/cover.jpg", "/album/movie.mkv"])(
    "shows collection browsing + video selection for mixed resources without first-file players or links: %s",
    (filePath) => {
      const videos = vi.fn();

      render(
        <PlayOnThisDevice
          isMixed
          filePath={filePath}
          kind="other"
          resource={{ id: 42 } as any}
          onPlayInBrowser={mocks.browser}
          onPlayNative={mocks.native}
          onPlayVideos={videos}
          onRememberChoice={mocks.save}
        />,
      );
      expect(screen.queryByText("PotPlayer")).toBeNull();
      expect(screen.queryByText("resource.playOnThisDevice.desktopPlayer")).toBeNull();
      expect(screen.queryByRole("textbox")).toBeNull();
      fireEvent.click(screen.getByText("resource.playback.playVideos"));
      expect(videos).toHaveBeenCalledOnce();
      expect(mocks.native).not.toHaveBeenCalled();
      expect(mocks.browser).not.toHaveBeenCalled();
    },
  );
  it("uses a one-time browser choice unless remembering is selected", () => {
    show();
    fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser"));
    expect(mocks.browser).toHaveBeenCalledOnce();
    expect(mocks.save).not.toHaveBeenCalled();
  });

  it("remembers images independently and keeps browser opening synchronous with the click", async () => {
    mocks.save.mockReturnValue(new Promise(() => {}));
    show();
    fireEvent.click(screen.getByRole("checkbox"));
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser")),
    );
    expect(mocks.save).toHaveBeenCalledWith("image", { mode: "browser" });
    expect(mocks.browser).toHaveBeenCalledOnce();
    expect(mocks.destroy).toHaveBeenCalledOnce();
  });

  it("preserves the desktop configured-player action without exposing AV URI handlers for an image", async () => {
    show(true);
    expect(screen.queryByText("PotPlayer")).toBeNull();
    fireEvent.click(screen.getByRole("checkbox"));
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.desktopPlayer")),
    );
    expect(mocks.save).toHaveBeenCalledWith("image", { mode: "native" });
    expect(mocks.native).toHaveBeenCalledOnce();
    expect(mocks.browser).not.toHaveBeenCalled();
  });

  it("allows Ask and Restore independently of the remember checkbox", async () => {
    show();
    await act(async () => fireEvent.click(screen.getByText("resource.playback.askEveryTime")));
    expect(mocks.save).toHaveBeenLastCalledWith("image", { mode: "ask" });
    await act(async () => fireEvent.click(screen.getByText("resource.playback.resetDefault")));
    expect(mocks.save).toHaveBeenLastCalledWith("image", { mode: "automatic" });
    expect(mocks.browser).not.toHaveBeenCalled();
    expect(mocks.native).not.toHaveBeenCalled();
  });

  it("reports failed persistence while still opening the one-time selected method", async () => {
    mocks.save.mockRejectedValue(new Error("full disk"));
    show();
    fireEvent.click(screen.getByRole("checkbox"));
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser")),
    );
    expect(mocks.danger).toHaveBeenCalledWith("resource.playOnThisDevice.preferenceFailed");
    expect(mocks.browser).toHaveBeenCalledOnce();
  });
});
