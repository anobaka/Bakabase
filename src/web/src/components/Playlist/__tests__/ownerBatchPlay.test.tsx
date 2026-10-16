import { Children, cloneElement } from "react";
import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import BatchPlayButton from "../components/BatchPlayButton";
import { BatchPlayFileSelectionMode } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  remote: true,
  getPlaylist: vi.fn(),
  candidates: vi.fn(),
  play: vi.fn(),
  localCandidates: vi.fn(),
  localPlay: vi.fn(),
  success: vi.fn(),
}));
const api = {
  playlist: { getPlaylist: mocks.getPlaylist },
  player: {
    getBatchPlayCandidates: mocks.candidates,
    batchPlayResources: mocks.play,
    getPlaylistBatchPlayCandidates: mocks.localCandidates,
    batchPlayPlaylist: mocks.localPlay,
  },
};
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/features/management/resourceContext", () => ({
  useResourceApi: () => api,
  useResourceOwner: () => (mocks.remote ? { serverId: "other", isLocal: false } : undefined),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => children,
  Dropdown: ({ children, onOpenChange }: any) => (
    <div>
      <button onClick={() => onOpenChange(true)}>open</button>
      {children}
    </div>
  ),
  DropdownTrigger: ({ children }: any) => children,
  DropdownMenu: ({ children, onAction }: any) => (
    <div>
      {Children.map(children, (child: any) =>
        cloneElement(child, { action: () => onAction(child.key) }),
      )}
    </div>
  ),
  DropdownItem: ({ children, isDisabled, action }: any) => (
    <button role="menuitem" disabled={isDisabled} onClick={action}>
      {children}
    </button>
  ),
  toast: { success: mocks.success },
}));
beforeEach(() => {
  vi.clearAllMocks();
  mocks.remote = true;
  mocks.getPlaylist.mockResolvedValue({
    code: 0,
    data: { items: [{ resourceId: 7 }, { resourceId: 9 }, { resourceId: 7 }] },
  });
  mocks.candidates.mockResolvedValue({
    code: 0,
    data: [{ key: "potplayer", displayName: "PotPlayer", matchedResourceCount: 2 }],
  });
  mocks.play.mockResolvedValue({ code: 0, data: { playerName: "PotPlayer", fileCount: 12 } });
});
afterEach(cleanup);

describe("playlist native playback on the resource owner", () => {
  it("uses resource candidate counts and submits one ordered all-files launch with repeated items", async () => {
    render(<BatchPlayButton playlistId={7} />);
    fireEvent.click(screen.getByText("open"));
    const player = await screen.findByRole("menuitem", { name: /PotPlayer/ });
    expect(player).not.toBeDisabled();
    expect(mocks.candidates).toHaveBeenCalledWith({ resourceIds: [7, 9, 7] });
    await act(async () => fireEvent.click(player));
    expect(mocks.play).toHaveBeenCalledExactlyOnceWith({
      resourceIds: [7, 9, 7],
      playerKey: "potplayer",
      fileSelectionMode: BatchPlayFileSelectionMode.AllFiles,
    });
    expect(mocks.localCandidates).not.toHaveBeenCalled();
    expect(mocks.localPlay).not.toHaveBeenCalled();
  });

  it("explains file-only entries and retry never becomes a fake player launch", async () => {
    mocks.getPlaylist.mockResolvedValue({
      code: 0,
      data: { items: [{ file: "/private/clip.mp4" }] },
    });
    render(<BatchPlayButton playlistId={7} />);
    fireEvent.click(screen.getByText("open"));
    expect(await screen.findByText("management.playlist.fileItems")).toBeDisabled();
    await act(async () => fireEvent.click(screen.getByText("management.retry")));
    expect(mocks.getPlaylist).toHaveBeenCalledTimes(2);
    expect(mocks.candidates).not.toHaveBeenCalled();
    expect(mocks.play).not.toHaveBeenCalled();
    expect(mocks.localPlay).not.toHaveBeenCalled();
  });

  it("retains the existing local playlist APIs and file counts", async () => {
    mocks.remote = false;
    mocks.localCandidates.mockResolvedValue({
      code: 0,
      data: [{ key: "local-player", displayName: "LocalPlayer", matchedFileCount: 4 }],
    });
    mocks.localPlay.mockResolvedValue({
      code: 0,
      data: { playerName: "LocalPlayer", fileCount: 4 },
    });
    render(<BatchPlayButton playlistId={7} />);
    fireEvent.click(screen.getByText("open"));
    const player = await screen.findByRole("menuitem", { name: /LocalPlayer/ });
    expect(player).not.toBeDisabled();
    await act(async () => fireEvent.click(player));
    expect(mocks.localPlay).toHaveBeenCalledExactlyOnceWith(7, { playerKey: "local-player" });
    expect(mocks.getPlaylist).not.toHaveBeenCalled();
    expect(mocks.play).not.toHaveBeenCalled();
  });
});
