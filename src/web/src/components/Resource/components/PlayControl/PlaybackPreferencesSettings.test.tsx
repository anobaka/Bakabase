import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlaybackPreferencesSettings from "./PlaybackPreferencesSettings";
import { automaticPlaybackPreferences, PLAYBACK_PREFERENCE_KEY } from "./playbackPreferences";
import { useDevicePlaybackStore } from "./usePlaybackPreferences";

const state = vi.hoisted(() => ({
  app: false,
  native: true,
  get: vi.fn(),
  save: vi.fn(),
  error: vi.fn(),
}));

vi.mock("@/utils/clientSurface", () => ({ isDesktopAppWindow: () => state.app }));
vi.mock("@/stores/remoteAccess", () => ({
  useUserSideActionsRunHere: () => state.native,
  useRemoteAccessStore: (selector: (value: unknown) => unknown) => selector({ initialized: true }),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    player: { getDevicePlaybackPreferences: state.get, setDevicePlaybackPreferences: state.save },
  },
}));
vi.mock("@/components/bakaui", () => ({
  toast: { danger: state.error },
  Select: ({ dataSource, selectedKeys, onSelectionChange, isDisabled, ...props }: any) => (
    <select
      aria-label={props["aria-label"]}
      disabled={isDisabled}
      value={Array.from(selectedKeys)[0] as string}
      onChange={(event) => onSelectionChange(new Set([event.target.value]))}
    >
      {dataSource.map((option: any) => (
        <option key={option.value} value={option.value}>
          {option.label}
        </option>
      ))}
    </select>
  ),
}));
beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  state.app = false;
  state.native = true;
  useDevicePlaybackStore.setState({
    scope: undefined,
    initialized: false,
    preferences: automaticPlaybackPreferences(),
  });
  state.get.mockResolvedValue({ code: 0, data: automaticPlaybackPreferences() });
  state.save.mockResolvedValue({ code: 0 });
});
afterEach(cleanup);

const videoPreference = () => screen.getAllByRole("combobox")[3]!;

describe("opening preferences in resource display options", () => {
  it("persists browser preferences locally and preserves choices for other kinds", async () => {
    localStorage.setItem(
      PLAYBACK_PREFERENCE_KEY,
      JSON.stringify({ ...automaticPlaybackPreferences(), image: { mode: "ask" } }),
    );
    render(<PlaybackPreferencesSettings />);
    await waitFor(() => expect(videoPreference()).not.toBeDisabled());
    expect(screen.getByText("resource.playbackSettings.browserScope")).toBeInTheDocument();
    fireEvent.change(videoPreference(), { target: { value: "browser" } });
    await waitFor(() => expect(videoPreference()).toHaveValue("browser"));
    expect(JSON.parse(localStorage.getItem(PLAYBACK_PREFERENCE_KEY)!)).toMatchObject({
      image: { mode: "ask" },
      video: { mode: "browser" },
    });
    expect(state.get).not.toHaveBeenCalled();
    expect(state.save).not.toHaveBeenCalled();
    fireEvent.change(videoPreference(), { target: { value: "automatic" } });
    await waitFor(() => expect(videoPreference()).toHaveValue("automatic"));
  });
  it("writes native app preferences through the existing device endpoint", async () => {
    state.app = true;
    render(<PlaybackPreferencesSettings />);
    await waitFor(() => expect(videoPreference()).not.toBeDisabled());
    expect(screen.getByText("resource.playbackSettings.deviceScope")).toBeInTheDocument();
    fireEvent.change(videoPreference(), { target: { value: "native" } });
    await waitFor(() => expect(videoPreference()).toHaveValue("native"));
    expect(state.save).toHaveBeenCalledWith({
      ...automaticPlaybackPreferences(),
      video: { mode: "native" },
    });
    expect(localStorage.getItem(PLAYBACK_PREFERENCE_KEY)).toBeNull();
  });
  it("retains the old preference when saving fails", async () => {
    state.app = true;
    state.save.mockResolvedValue({ code: 500, message: "full disk" });
    render(<PlaybackPreferencesSettings />);
    await waitFor(() => expect(videoPreference()).not.toBeDisabled());
    fireEvent.change(videoPreference(), { target: { value: "browser" } });
    await waitFor(() =>
      expect(state.error).toHaveBeenCalledWith("resource.playbackSettings.saveFailed"),
    );
    expect(videoPreference()).toHaveValue("automatic");
  });
  it("preserves an unavailable saved protocol until the user changes it", async () => {
    localStorage.setItem(
      PLAYBACK_PREFERENCE_KEY,
      JSON.stringify({
        ...automaticPlaybackPreferences(),
        video: { mode: "native", nativeSchemeId: "player-no-longer-supported" },
      }),
    );
    render(<PlaybackPreferencesSettings />);
    await waitFor(() => expect(videoPreference()).not.toBeDisabled());
    expect(videoPreference()).toHaveValue("native:player-no-longer-supported");
    expect(state.save).not.toHaveBeenCalled();
    fireEvent.change(videoPreference(), { target: { value: "ask" } });
    await waitFor(() => expect(videoPreference()).toHaveValue("ask"));
    expect(JSON.parse(localStorage.getItem(PLAYBACK_PREFERENCE_KEY)!).video).toEqual({
      mode: "ask",
    });
  });
});
