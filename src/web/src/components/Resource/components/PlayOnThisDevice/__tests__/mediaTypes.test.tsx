import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlayOnThisDevice from "..";

const history = vi.hoisted(() => vi.fn());

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      getRawFileUrl: ({ fullname }: { fullname: string }) =>
        `/file/raw?fullname=${encodeURIComponent(fullname)}`,
    },
    resource: { markResourceAsPlayed: history },
  },
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div role="dialog">{children}</div>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Checkbox: () => null,
  Select: ({ selectedKeys, dataSource, placeholder, onSelectionChange }: any) => (
    <select
      aria-label="device system"
      value={(Array.from(selectedKeys)[0] as string) ?? ""}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      <option value="">{placeholder}</option>
      {dataSource.map(({ value, label }: any) => (
        <option key={value} value={value}>
          {label}
        </option>
      ))}
    </select>
  ),
  toast: {},
}));

const ua = (value: string) => vi.spyOn(navigator, "userAgent", "get").mockReturnValue(value);
const show = (filePath: string) => {
  const browser = vi.fn();

  render(
    <PlayOnThisDevice
      filePath={filePath}
      resource={{ id: 1, displayName: "Example" } as any}
      onPlayInBrowser={browser}
    />,
  );

  return browser;
};

beforeEach(() => {
  localStorage.clear();
  history.mockClear();
  ua("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("current-device players for the selected file", () => {
  it.each(["/media/0001.JPG", "/folder.mp4/chapter.txt", "/media/archive.zip"])(
    "keeps browser viewing and file link but hides video players for %s",
    (path) => {
      const browser = show(path);

      expect(screen.queryByText("PotPlayer")).toBeNull();
      expect(screen.queryByText("VLC")).toBeNull();
      expect(screen.queryByText("resource.playOnThisDevice.nativePlayers")).toBeNull();
      expect(screen.queryByLabelText("device system")).toBeNull();
      expect(screen.getByLabelText("resource.playOnThisDevice.fileUrl")).toHaveValue(
        `${window.location.origin}/file/raw?fullname=${encodeURIComponent(path)}`,
      );
      fireEvent.click(screen.getByText("resource.playOnThisDevice.viewInBrowser"));
      expect(browser).toHaveBeenCalledTimes(1);
      expect(history).not.toHaveBeenCalled();
    },
  );

  it.each(["/media/movie.mkv", "/media/track.FLAC"])(
    "still offers Windows native players and browser playback for %s",
    (path) => {
      show(path);
      expect(screen.getByText("PotPlayer")).toBeInTheDocument();
      expect(screen.getByText("VLC")).toBeInTheDocument();
      expect(screen.getByText("resource.playOnThisDevice.playInBrowser")).toBeInTheDocument();
      expect(screen.getByLabelText("device system")).toHaveValue("windows");
    },
  );

  it("uses the browser's known Mac platform rather than a stale saved Windows selection", () => {
    ua("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)");
    localStorage.setItem("bakabase-remote-platform", "windows");
    show("/media/movie.mkv");
    expect(screen.queryByText("PotPlayer")).toBeNull();
    expect(screen.getByText("IINA")).toBeInTheDocument();
    expect(screen.getByLabelText("device system")).toHaveValue("macos");
  });

  it("does not guess Windows for an unknown device, and permits an explicit system choice", () => {
    ua("UnidentifiedClient/1.0");
    show("/media/movie.mkv");
    expect(screen.queryByText("PotPlayer")).toBeNull();
    expect(screen.queryByText("VLC")).toBeNull();
    expect(screen.getByLabelText("device system")).toHaveValue("");
    fireEvent.change(screen.getByLabelText("device system"), { target: { value: "windows" } });
    expect(screen.getByText("PotPlayer")).toBeInTheDocument();
    expect(history).not.toHaveBeenCalled();
  });

  it("still opens when browser storage cannot be read or written", () => {
    ua("UnidentifiedClient/1.0");
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("unavailable");
    });
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("unavailable");
    });
    show("/media/movie.mkv");
    fireEvent.change(screen.getByLabelText("device system"), { target: { value: "macos" } });
    expect(screen.getByText("IINA")).toBeInTheDocument();
  });
});
