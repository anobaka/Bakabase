import {
  act,
  cleanup,
  fireEvent,
  render,
  renderHook,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import {
  normalizeOpeningRules,
  openingChoice,
  PlaybackOverridesButton,
  useOpeningRulesStore,
  useTargetPlayback,
} from "./overrides";

const fixture = vi.hoisted(() => ({ native: true, app: true, get: vi.fn(), save: vi.fn() }));

vi.mock("@/stores/remoteAccess", () => ({ useUserSideActionsRunHere: () => fixture.native }));
vi.mock("@/utils/clientSurface", () => ({ isDesktopAppWindow: () => fixture.app }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    player: { getDevicePlaybackOverrides: fixture.get, setDevicePlaybackOverrides: fixture.save },
  },
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: Record<string, string>) =>
      values?.kind ? `${key}:${values.kind}` : key,
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children, title }: any) => (
    <div aria-label={title} role="dialog">
      {children}
    </div>
  ),
  Button: ({ children, onPress, isDisabled, isLoading, ...props }: any) => (
    <button aria-label={props["aria-label"]} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
  Tooltip: ({ children }: any) => children,
}));
const target = "server:media-server";
const key = `desktop:${target}`;
const storageKey = "bakabase.playback.overrides.v1";
const inherited = { target, choices: { video: { mode: "native" as const, playerId: "vlc" } } };
const resource = { target, resourceId: 42, choices: { image: { mode: "browser" as const } } };
const deferred = <T,>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });

  return { promise, resolve };
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  fixture.native = true;
  fixture.app = true;
  fixture.get.mockResolvedValue({ code: 0, data: { rules: [], openers: [] } });
  fixture.save.mockResolvedValue({ code: 0 });
  useOpeningRulesStore.setState({ snapshots: {}, errors: {} });
});
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});

describe("target opening rule normalization and precedence", () => {
  it("accepts only target identities, positive resource IDs and known mode/kind shapes", () => {
    expect(
      normalizeOpeningRules([
        inherited,
        resource,
        { target: "node:peer:epoch", choices: { other: { mode: "ask" } } },
      ]),
    ).toHaveLength(3);
    expect(
      normalizeOpeningRules([
        { ...inherited, target: "https://server" },
        { ...inherited, resourceId: 0 },
        { ...inherited, resourceId: 1.5 },
        { ...inherited, choices: { execute: { mode: "native" } } },
        { ...inherited, choices: { video: { mode: "shell" } } },
        { ...inherited, choices: { video: { mode: "native", playerId: "/bin/tool" } } },
        { ...inherited, choices: [] },
        { ...inherited, choices: { video: { mode: "browser", playerId: "vlc" } } },
      ]),
    ).toEqual([]);
    expect(normalizeOpeningRules(Array(1025).fill(inherited))).toEqual([]);
  });
  it("uses resource choices before target defaults and inherits other kinds independently", () => {
    const rules = [
      inherited,
      resource,
      { target, resourceId: 43, choices: { video: { mode: "browser" as const } } },
    ];

    expect(openingChoice(rules, 42, "image")).toEqual({ mode: "browser" });
    expect(openingChoice(rules, 42, "video")).toEqual({ mode: "native", playerId: "vlc" });
    expect(openingChoice(rules, 43, "video")).toEqual({ mode: "browser" });
    expect(openingChoice(rules, 44, "text")).toBeUndefined();
  });
});

describe("viewing-device override persistence", () => {
  it("keeps a first failed read unavailable until retry succeeds", async () => {
    fixture.get.mockResolvedValueOnce({ code: 404 });
    const { result } = renderHook(() => useTargetPlayback(target, 42));

    await waitFor(() => expect(result.current.unavailable).toBe(true));
    expect(result.current.ready).toBe(false);
    expect(useOpeningRulesStore.getState().snapshots[key]).toBeUndefined();
    fixture.get.mockResolvedValueOnce({ code: 0, data: { rules: [inherited], openers: [] } });
    await act(() => result.current.reload());
    expect(result.current.ready).toBe(true);
    expect(result.current.unavailable).toBe(false);
    expect(result.current.choice("video")).toEqual(inherited.choices.video);
  });
  it("retains the last successful snapshot if a later refresh fails", async () => {
    fixture.get.mockResolvedValueOnce({ code: 0, data: { rules: [resource], openers: [] } });
    const { result } = renderHook(() => useTargetPlayback(target, 42));

    await waitFor(() => expect(result.current.ready).toBe(true));
    fixture.get.mockRejectedValueOnce(new Error("offline"));
    await act(() => result.current.reload());
    expect(result.current.error).toBe(true);
    expect(result.current.unavailable).toBe(false);
    expect(result.current.choice("image")).toEqual({ mode: "browser" });
  });
  it("coalesces same-target reads and reuses their cache", async () => {
    const read = deferred<any>();

    fixture.get.mockReturnValue(read.promise);
    const a = useOpeningRulesStore.getState().load(key, target, true);
    const b = useOpeningRulesStore.getState().load(key, target, true);

    expect(fixture.get).toHaveBeenCalledOnce();
    read.resolve({ code: 0, data: { rules: [inherited], openers: [] } });
    await Promise.all([a, b]);
    await useOpeningRulesStore.getState().load(key, target, true);
    expect(fixture.get).toHaveBeenCalledOnce();
    expect(useOpeningRulesStore.getState().snapshots[key].rules).toEqual([inherited]);
  });
  it("isolates browser preferences even when a localhost browser has a native bridge", async () => {
    fixture.app = false;
    localStorage.setItem(storageKey, JSON.stringify([inherited]));
    const { result } = renderHook(() => useTargetPlayback(target, 42));

    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(result.current.desktop).toBe(false);
    expect(result.current.choice("video")).toEqual(inherited.choices.video);
    await act(() => result.current.save(resource));
    expect(fixture.get).not.toHaveBeenCalled();
    expect(fixture.save).not.toHaveBeenCalled();
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual([inherited, resource]);
  });
  it("reads desktop rules without importing browser overrides", async () => {
    localStorage.setItem(storageKey, JSON.stringify([inherited]));
    fixture.get.mockResolvedValue({ code: 0, data: { rules: [resource], openers: [] } });
    const { result } = renderHook(() => useTargetPlayback(target, 42));

    await waitFor(() => expect(result.current.ready).toBe(true));
    expect(result.current.choice("video")).toBeUndefined();
    expect(result.current.choice("image")).toEqual({ mode: "browser" });
    expect(result.current.preference("text", { mode: "ask" })).toEqual({ mode: "ask" });
    expect(localStorage.getItem(storageKey)).toBe(JSON.stringify([inherited]));
  });
  it("resetting a resource preserves its target default and other targets", async () => {
    const other = { ...inherited, target: "server:other" };

    localStorage.setItem(storageKey, JSON.stringify([inherited, resource, other]));
    await useOpeningRulesStore
      .getState()
      .save(`browser:${target}`, { ...resource, choices: {} }, false);
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual([inherited, other]);
    expect(useOpeningRulesStore.getState().snapshots[`browser:${target}`].rules).toEqual([
      inherited,
    ]);
  });
  it("preserves cached choices on save refusal and browser storage failure", async () => {
    fixture.get.mockResolvedValue({ code: 0, data: { rules: [inherited], openers: [] } });
    await useOpeningRulesStore.getState().load(key, target, true);
    fixture.save.mockResolvedValue({ code: 500 });
    await expect(useOpeningRulesStore.getState().save(key, resource, true)).rejects.toThrow();
    expect(useOpeningRulesStore.getState().snapshots[key].rules).toEqual([inherited]);
    localStorage.setItem(storageKey, JSON.stringify([inherited]));
    vi.spyOn(Storage.prototype, "setItem").mockImplementationOnce(() => {
      throw new Error("quota");
    });
    await expect(
      useOpeningRulesStore.getState().save(`browser:${target}`, resource, false),
    ).rejects.toThrow("quota");
    expect(JSON.parse(localStorage.getItem(storageKey)!)).toEqual([inherited]);
  });
  it("does not let a stale in-flight read replace a successful saved rule", async () => {
    fixture.get.mockResolvedValueOnce({ code: 0, data: { rules: [inherited], openers: [] } });
    await useOpeningRulesStore.getState().load(key, target, true);
    const oldRead = deferred<any>();

    fixture.get.mockReturnValueOnce(oldRead.promise);
    const refreshing = useOpeningRulesStore.getState().load(key, target, true, true);
    const saving = useOpeningRulesStore.getState().save(key, resource, true);

    oldRead.resolve({ code: 0, data: { rules: [inherited], openers: [] } });
    await Promise.all([refreshing, saving]);
    expect(useOpeningRulesStore.getState().snapshots[key].rules).toContainEqual(resource);
  });
  it("filters a device response to the requested target before matching colliding resource IDs", async () => {
    fixture.get.mockResolvedValue({
      code: 0,
      data: { rules: [{ ...resource, target: "server:other" }, inherited], openers: [] },
    });
    await useOpeningRulesStore.getState().load(key, target, true);
    expect(useOpeningRulesStore.getState().snapshots[key].rules).toEqual([inherited]);
  });
});

describe("opening rule editor", () => {
  it("filters known players by media kind and retains a saved unavailable choice visibly", async () => {
    fixture.get.mockResolvedValue({
      code: 0,
      data: {
        rules: [
          {
            target,
            choices: {
              video: { mode: "native", playerId: "foobar2000" },
              audio: { mode: "native" },
            },
          },
        ],
        openers: [
          {
            id: "vlc",
            name: "VLC",
            installed: true,
            supportsHttp: true,
            supportedKinds: ["audio", "video"],
          },
          {
            id: "foobar2000",
            name: "Foobar2000",
            installed: true,
            supportsHttp: true,
            supportedKinds: ["audio"],
          },
        ],
      },
    });
    render(<PlaybackOverridesButton target={target} />);
    fireEvent.click(screen.getByText("resource.openingRules.title"));
    const video = await screen.findByLabelText(
      "resource.openingRules.playerFor:resource.playback.kind.video",
    );

    expect(video).toHaveValue("foobar2000");
    expect(video.querySelector('option[value="foobar2000"]')).toBeDisabled();
    expect(video.querySelector('option[value="foobar2000"]')).toHaveTextContent(
      "resource.openingRules.playerUnavailable",
    );
    const audio = screen.getByLabelText(
      "resource.openingRules.playerFor:resource.playback.kind.audio",
    );

    expect(audio.querySelector('option[value="foobar2000"]')).not.toBeDisabled();
  });
  it("provides a named compact settings icon for resource toolbars", async () => {
    render(<PlaybackOverridesButton compact target={target} />);
    const button = screen.getByRole("button", { name: "resource.openingRules.title" });

    expect(button.querySelector("svg")).toBeInTheDocument();
    fireEvent.click(button);
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
  });
  it("retains an unavailable native choice without displaying it as inherited", async () => {
    fixture.app = false;
    fixture.native = false;
    localStorage.setItem(storageKey, JSON.stringify([inherited]));
    render(<PlaybackOverridesButton target={target} />);
    fireEvent.click(screen.getByText("resource.openingRules.title"));
    const method = await screen.findByLabelText(
      "resource.openingRules.methodFor:resource.playback.kind.video",
    );

    await waitFor(() => expect(method).toHaveValue("native"));
    expect(method.querySelector('option[value="native"]')).toBeDisabled();
    expect(screen.getByText("resource.openingRules.browserHint")).toBeInTheDocument();
  });
  it("keeps failed saves visible with their draft choices intact", async () => {
    fixture.save.mockResolvedValue({ code: 500 });
    render(<PlaybackOverridesButton target={target} />);
    fireEvent.click(screen.getByText("resource.openingRules.title"));
    const method = screen.getByLabelText(
      "resource.openingRules.methodFor:resource.playback.kind.video",
    );

    await waitFor(() => expect(method).not.toBeDisabled());
    fireEvent.change(method, { target: { value: "ask" } });
    fireEvent.click(screen.getByText("common.action.save"));
    expect(await screen.findByText("resource.openingRules.saveFailed")).toBeInTheDocument();
    expect(method).toHaveValue("ask");
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });
  it("shows only known installed choices for native AV and keeps browser settings scoped locally", async () => {
    fixture.get.mockResolvedValue({
      code: 0,
      data: {
        rules: [inherited],
        openers: [
          { id: "vlc", name: "VLC", installed: true, supportsHttp: true },
          { id: "potplayer", name: "PotPlayer", installed: false, supportsHttp: true },
        ],
      },
    });
    render(<PlaybackOverridesButton resourceId={42} target={target} />);
    fireEvent.click(screen.getByText("resource.openingRules.title"));
    const player = await screen.findByLabelText(
      "resource.openingRules.playerFor:resource.playback.kind.video",
    );

    expect(player).toHaveValue("vlc");
    expect(
      screen.getByRole("option", { name: "PotPlayer · resource.openingRules.notInstalled" }),
    ).toBeDisabled();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("resource.openingRules.scope"), {
      target: { value: "resource" },
    });
    fireEvent.change(
      screen.getByLabelText("resource.openingRules.methodFor:resource.playback.kind.image"),
      { target: { value: "native" } },
    );
    const imagePlayer = screen.getByLabelText(
      "resource.openingRules.playerFor:resource.playback.kind.image",
    );

    expect(imagePlayer.querySelectorAll("option")).toHaveLength(2);
    fireEvent.change(imagePlayer, { target: { value: "system-default" } });
    await act(async () => fireEvent.click(screen.getByText("common.action.save")));
    expect(fixture.save).toHaveBeenCalledWith({
      target,
      resourceId: 42,
      choices: { image: { mode: "native", playerId: "system-default" } },
    });
  });
  it("keeps a user's draft when a background reload completes", async () => {
    useOpeningRulesStore.setState({ snapshots: { [key]: { rules: [], openers: [] } }, errors: {} });
    const read = deferred<any>();

    fixture.get.mockReturnValue(read.promise);
    render(<PlaybackOverridesButton resourceId={42} target={target} />);
    fireEvent.click(screen.getByText("resource.openingRules.title"));
    const method = screen.getByLabelText(
      "resource.openingRules.methodFor:resource.playback.kind.video",
    );

    fireEvent.change(method, { target: { value: "ask" } });
    await act(async () => read.resolve({ code: 0, data: { rules: [inherited], openers: [] } }));
    expect(method).toHaveValue("ask");
  });
});
