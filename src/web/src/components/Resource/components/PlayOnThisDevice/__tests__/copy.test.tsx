import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlayOnThisDevice from "..";

const mocks = vi.hoisted(() => ({ success: vi.fn(), danger: vi.fn(), markPlayed: vi.fn() }));

vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    file: {
      getRawFileUrl: ({ fullname }: any) => `/file/raw?fullname=${encodeURIComponent(fullname)}`,
    },
    resource: { markResourceAsPlayed: mocks.markPlayed },
  },
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div role="dialog">{children}</div>,
  Button: ({ children, onPress }: any) => <button onClick={onPress}>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Select: () => null,
  toast: { success: mocks.success, danger: mocks.danger },
}));

const clipboard = Object.getOwnPropertyDescriptor(navigator, "clipboard");
const command = Object.getOwnPropertyDescriptor(document, "execCommand");
const show = () =>
  render(
    <PlayOnThisDevice
      filePath="/media/a b.mp4"
      resource={{ id: 42, displayName: "Movie" } as any}
      onPlayInBrowser={vi.fn()}
    />,
  );

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});
afterEach(() => {
  cleanup();
  if (clipboard) Object.defineProperty(navigator, "clipboard", clipboard);
  else Reflect.deleteProperty(navigator, "clipboard");
  if (command) Object.defineProperty(document, "execCommand", command);
  else Reflect.deleteProperty(document, "execCommand");
});

describe("playback link copying", () => {
  it("copies an absolute playable URL with visible success feedback", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    show();
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.copyStreamUrl")),
    );
    const url = `${window.location.origin}/file/raw?fullname=%2Fmedia%2Fa%20b.mp4`;

    expect(writeText).toHaveBeenCalledExactlyOnceWith(url);
    expect(screen.getByLabelText("resource.playOnThisDevice.streamUrl")).toHaveValue(url);
    expect(mocks.success).toHaveBeenCalledExactlyOnceWith("resource.playOnThisDevice.copied");
    expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent(
      "resource.playOnThisDevice.copied",
    );
    expect(mocks.markPlayed).not.toHaveBeenCalled();
  });

  it("copies on an HTTP LAN origin inside the modal focus scope", async () => {
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: undefined });
    const exec = vi.fn(() => {
      expect(document.activeElement?.closest('[role="dialog"]')).not.toBeNull();
      expect(document.activeElement).toBeInstanceOf(HTMLTextAreaElement);

      return true;
    });

    Object.defineProperty(document, "execCommand", { configurable: true, value: exec });
    show();
    const button = screen.getByText("resource.playOnThisDevice.copyStreamUrl");

    button.focus();
    await act(async () => fireEvent.click(button));
    expect(exec).toHaveBeenCalledExactlyOnceWith("copy");
    expect(document.activeElement).toBe(button);
    expect(document.querySelectorAll("textarea")).toHaveLength(1);
    expect(mocks.success).toHaveBeenCalledTimes(1);
  });

  it("keeps a selectable URL and explains failure when both clipboard paths refuse", async () => {
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText: vi.fn().mockRejectedValue(new Error("Permission denied")) },
    });
    Object.defineProperty(document, "execCommand", { configurable: true, value: () => false });
    show();
    await act(async () =>
      fireEvent.click(screen.getByText("resource.playOnThisDevice.copyStreamUrl")),
    );
    expect(mocks.danger).toHaveBeenCalledExactlyOnceWith("resource.playOnThisDevice.copyFailed");
    expect(mocks.success).not.toHaveBeenCalled();
    expect(screen.getByLabelText("resource.playOnThisDevice.streamUrl")).not.toHaveValue("");
    expect(document.querySelector('[aria-live="polite"]')).toHaveTextContent(
      "resource.playOnThisDevice.copyFailed",
    );
  });
});
