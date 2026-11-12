import { fireEvent, render, screen } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import ExHentaiDownloadModeField from "../ExHentaiDownloadModeField";
import { ExHentaiDownloadMode, ExHentaiNoTorrentAction } from "@/sdk/constants";
import type {
  ExHentaiDownloadOptions,
  ExHentaiDownloadPatch,
} from "@/pages/downloader/exHentaiDownloadMode";

vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Checkbox: ({
    children,
    isSelected,
    isDisabled,
    onValueChange,
  }: {
    children: ReactNode;
    isSelected: boolean;
    isDisabled?: boolean;
    onValueChange: (value: boolean) => void;
  }) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
}));

const keys = {
  prefer: "downloader.policy.preferBt",
  save: "downloader.policy.saveTorrent",
  download: "downloader.policy.downloadBt",
  adopt: "downloader.policy.adoptFallback",
};
const checkbox = (key: string) => screen.getByRole("checkbox", { name: key });
function mount(initial?: ExHentaiDownloadOptions, isExistingTask = false) {
  const onChange = vi.fn();
  function Harness() {
    const [options, setOptions] = useState(initial);
    const update = (patch: ExHentaiDownloadPatch) => {
      onChange(patch);
      setOptions((current) => ({ ...current, ...patch }));
    };
    return (
      <ExHentaiDownloadModeField
        options={options}
        isExistingTask={isExistingTask}
        onChange={update}
      />
    );
  }
  render(<Harness />);
  return onChange;
}

describe("ExHentai BT preference", () => {
  it("defaults new settings to internal BT without writing settings on mount", () => {
    const onChange = mount();
    expect(checkbox(keys.prefer)).toBeChecked();
    expect(checkbox(keys.save)).not.toBeChecked();
    expect(checkbox(keys.download)).toBeChecked();
    expect(checkbox(keys.download)).toBeDisabled();
    expect(screen.getAllByRole("checkbox")).toHaveLength(3);
    expect(screen.queryByRole("combobox")).toBeNull();
    expect(screen.getByText("downloader.policy.costHint")).toBeVisible();
    expect(screen.getByText("downloader.policy.fallbackHint")).toBeVisible();
    expect(onChange).not.toHaveBeenCalled();
  });

  it("supports both BT actions and never lets the last action be unchecked", () => {
    const onChange = mount();
    fireEvent.click(checkbox(keys.save));
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      preferTorrent: true,
      saveTorrentFile: true,
      fallbackToDirect: true,
      noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
    });
    expect(checkbox(keys.download)).toBeEnabled();
    fireEvent.click(checkbox(keys.download));
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.TorrentFile,
      preferTorrent: true,
      saveTorrentFile: false,
      fallbackToDirect: true,
      noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
    });
    expect(checkbox(keys.save)).toBeChecked();
    expect(checkbox(keys.save)).toBeDisabled();
    expect(checkbox(keys.download)).not.toBeChecked();
    expect(screen.getByText("downloader.policy.torrentFileHint")).toBeVisible();
    fireEvent.click(checkbox(keys.prefer));
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({
        downloadMode: ExHentaiDownloadMode.Direct,
        preferTorrent: false,
        noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
      }),
    );
    expect(screen.getAllByRole("checkbox")).toHaveLength(1);
    expect(screen.getByText("downloader.policy.directHint")).toBeVisible();
    fireEvent.click(checkbox(keys.prefer));
    expect(checkbox(keys.save)).toBeChecked();
    expect(checkbox(keys.download)).not.toBeChecked();
    expect(onChange).toHaveBeenLastCalledWith(
      expect.objectContaining({ downloadMode: ExHentaiDownloadMode.TorrentFile }),
    );
  });

  it.each([
    undefined,
    { preferTorrent: true },
    { downloadMode: ExHentaiDownloadMode.TorrentFile, fallbackToDirect: true },
    { downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: false },
    { downloadMode: ExHentaiDownloadMode.Torrent, noTorrentAction: ExHentaiNoTorrentAction.Fail },
  ])("preserves an old no-fallback task until the user adopts the new policy (%o)", (options) => {
    const onChange = mount(options, true);
    expect(screen.getByRole("note")).toHaveTextContent("downloader.policy.legacyNoFallback");
    expect(onChange).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: keys.adopt }));
    expect(onChange).toHaveBeenCalledExactlyOnceWith(
      expect.objectContaining({
        noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
        fallbackToDirect: true,
        preferTorrent: true,
      }),
    );
    expect(screen.queryByRole("note")).toBeNull();
    expect(screen.getByText("downloader.policy.fallbackHint")).toBeVisible();
  });

  it("shows fallback for new tasks using a saved torrent-file preference", () => {
    const onChange = mount({
      downloadMode: ExHentaiDownloadMode.TorrentFile,
      fallbackToDirect: false,
    });
    expect(checkbox(keys.save)).toBeChecked();
    expect(checkbox(keys.download)).not.toBeChecked();
    expect(screen.queryByRole("note")).toBeNull();
    expect(screen.getByText("downloader.policy.fallbackHint")).toBeVisible();
    expect(onChange).not.toHaveBeenCalled();
  });

  it.each([
    { downloadMode: ExHentaiDownloadMode.Torrent, fallbackToDirect: true },
    {
      downloadMode: ExHentaiDownloadMode.TorrentFile,
      fallbackToDirect: false,
      noTorrentAction: ExHentaiNoTorrentAction.DirectDownload,
    },
  ])("recognizes tasks that already allow fallback without changing them (%o)", (options) => {
    const onChange = mount(options, true);
    expect(screen.queryByRole("button", { name: keys.adopt })).toBeNull();
    expect(screen.getByText("downloader.policy.fallbackHint")).toBeVisible();
    expect(onChange).not.toHaveBeenCalled();
  });
});
