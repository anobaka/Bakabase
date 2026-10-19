import { fireEvent, render, screen } from "@testing-library/react";
import { useState, type ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";

import ExHentaiDownloadModeField from "../ExHentaiDownloadModeField";
import { ExHentaiDownloadMode } from "@/sdk/constants";
import type {
  ExHentaiDownloadOptions,
  ExHentaiDownloadPatch,
} from "@/pages/downloader/exHentaiDownloadMode";

vi.mock("@/components/bakaui", () => ({
  Select: ({ label, dataSource, selectedKeys, onSelectionChange }: any) => (
    <select
      aria-label={label}
      value={selectedKeys[0]}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((option: { value: string; label: string }) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  ),
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

function mount(initial?: ExHentaiDownloadOptions) {
  const onChange = vi.fn();
  function Harness() {
    const [options, setOptions] = useState(initial);
    const update = (patch: ExHentaiDownloadPatch) => {
      onChange(patch);
      setOptions((current) => ({ ...current, ...patch }));
    };
    return <ExHentaiDownloadModeField options={options} onChange={update} />;
  }
  render(<Harness />);
  return onChange;
}

describe("ExHentai acquisition routes", () => {
  it("offers only direct and BT routes and starts a newly selected BT route without paid fallback", () => {
    const onChange = mount({ downloadMode: ExHentaiDownloadMode.Direct });
    const route = screen.getByRole("combobox", { name: "downloader.mode.label" });
    expect(screen.getAllByRole("option").map((option) => option.textContent)).toEqual([
      "downloader.mode.direct",
      "downloader.mode.bt",
    ]);
    expect(screen.queryByRole("checkbox")).toBeNull();
    fireEvent.change(route, { target: { value: String(ExHentaiDownloadMode.Torrent) } });
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: false,
      fallbackToDirect: false,
    });
    expect(
      screen.getByRole("checkbox", { name: "downloader.mode.downloadTorrent" }),
    ).not.toBeChecked();
    expect(screen.getByRole("checkbox", { name: "downloader.mode.createBtTask" })).toBeChecked();
    expect(screen.getByRole("checkbox", { name: "downloader.mode.createBtTask" })).toBeDisabled();
    expect(
      screen.getByRole("checkbox", { name: "downloader.mode.fallbackToDirect" }),
    ).not.toBeChecked();
  });

  it.each([
    undefined,
    { preferTorrent: true },
    { downloadMode: ExHentaiDownloadMode.TorrentFile },
    { downloadMode: ExHentaiDownloadMode.TorrentFile, fallbackToDirect: true },
  ])(
    "keeps saved torrent-only settings on the BT route without silently changing them (%o)",
    (options) => {
      const onChange = mount(options);
      const route = screen.getByRole("combobox", { name: "downloader.mode.label" });
      expect(route).toHaveValue(String(ExHentaiDownloadMode.Torrent));
      expect(
        screen.getByRole("checkbox", { name: "downloader.mode.downloadTorrent" }),
      ).toBeChecked();
      expect(
        screen.getByRole("checkbox", { name: "downloader.mode.downloadTorrent" }),
      ).toBeDisabled();
      expect(
        screen.getByRole("checkbox", { name: "downloader.mode.createBtTask" }),
      ).not.toBeChecked();
      expect(
        screen.queryByRole("checkbox", { name: "downloader.mode.fallbackToDirect" }),
      ).toBeNull();
      fireEvent.change(route, { target: { value: String(ExHentaiDownloadMode.Torrent) } });
      expect(onChange).not.toHaveBeenCalled();
      expect(
        screen.getByRole("checkbox", { name: "downloader.mode.downloadTorrent" }),
      ).toBeChecked();
    },
  );

  it("supports all three BT combinations without clearing an explicit fallback preference", () => {
    const onChange = mount({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: false,
      fallbackToDirect: true,
    });
    const torrent = () => screen.getByRole("checkbox", { name: "downloader.mode.downloadTorrent" });
    const transfer = () => screen.getByRole("checkbox", { name: "downloader.mode.createBtTask" });
    expect(transfer()).toBeChecked();
    expect(transfer()).toBeDisabled();
    fireEvent.click(torrent());
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: true,
    });
    expect(torrent()).toBeChecked();
    expect(torrent()).toBeEnabled();
    expect(transfer()).toBeChecked();
    expect(transfer()).toBeEnabled();
    expect(
      screen.getByRole("checkbox", { name: "downloader.mode.fallbackToDirect" }),
    ).toBeChecked();
    fireEvent.click(transfer());
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.TorrentFile,
      saveTorrentFile: false,
    });
    expect(torrent()).toBeChecked();
    expect(torrent()).toBeDisabled();
    expect(transfer()).not.toBeChecked();
    expect(screen.queryByRole("checkbox", { name: "downloader.mode.fallbackToDirect" })).toBeNull();
    fireEvent.click(transfer());
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: true,
      fallbackToDirect: true,
    });
    expect(
      screen.getByRole("checkbox", { name: "downloader.mode.fallbackToDirect" }),
    ).toBeChecked();
    fireEvent.click(torrent());
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: false,
    });
    expect(transfer()).toBeDisabled();
    expect(screen.getByText("downloader.mode.btSelectionHint")).toBeVisible();
    fireEvent.change(screen.getByRole("combobox"), {
      target: { value: String(ExHentaiDownloadMode.Direct) },
    });
    expect(onChange).toHaveBeenLastCalledWith({ downloadMode: ExHentaiDownloadMode.Direct });
    expect(screen.queryByRole("checkbox")).toBeNull();
    fireEvent.change(screen.getByRole("combobox"), {
      target: { value: String(ExHentaiDownloadMode.Torrent) },
    });
    expect(onChange).toHaveBeenLastCalledWith({
      downloadMode: ExHentaiDownloadMode.Torrent,
      saveTorrentFile: false,
      fallbackToDirect: true,
    });
  });
});
