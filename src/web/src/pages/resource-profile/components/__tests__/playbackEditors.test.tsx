import type { ReactNode } from "react";
import type { BakabaseAbstractionsModelsDomainMediaLibraryPlayer } from "@/sdk/Api";

import { useState } from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PlayerSelectorModal, { generatePlayerCommand } from "../PlayerSelectorModal";
import PlayableFileSelectorModal from "../PlayableFileSelectorModal";

vi.mock("@/components/utils", () => ({
  splitPathIntoSegments: (path: string) => path.split(/[/\\]/).filter(Boolean),
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({
    visible,
    children,
    footer,
  }: {
    visible: boolean;
    children: ReactNode;
    footer: ReactNode;
  }) =>
    visible ? (
      <section role="dialog">
        {children}
        {footer}
      </section>
    ) : null,
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title, subtitle, "aria-label": label }: any) => (
    <section aria-label={label}>
      {title}
      <p>{subtitle}</p>
      {children}
    </section>
  ),
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Input: ({ label, value, onValueChange, isDisabled, isInvalid, errorMessage }: any) => (
    <label>
      {label}
      <input
        aria-label={label}
        disabled={isDisabled}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
      {isInvalid && <span>{errorMessage}</span>}
    </label>
  ),
  Button: ({ children, onPress, isDisabled, isLoading, "aria-label": label }: any) => (
    <button aria-label={label} disabled={isDisabled || isLoading} onClick={() => void onPress?.()}>
      {children}
    </button>
  ),
}));
vi.mock("@/components/PathAutocomplete", () => ({
  default: ({ value, onChange, label, isDisabled, isInvalid, errorMessage }: any) => (
    <label>
      {label}
      <input
        aria-label={label}
        disabled={isDisabled}
        value={value}
        onChange={(event) => {
          const path = event.target.value;

          onChange(
            path,
            path === "unresolved" ? undefined : path.endsWith("/") ? "folder" : "file",
          );
        }}
      />
      {isInvalid && <span>{errorMessage}</span>}
    </label>
  ),
}));
vi.mock("@/components/ExtensionsInput", () => ({
  default: function ExtensionsInputMock({ defaultValue, label, onValueChange }: any) {
    // Match the real component's local draft. Index keys/remounting on every keystroke lose that draft.
    const [text, setText] = useState<string>((defaultValue ?? []).join(" "));

    return (
      <input
        aria-label={label}
        value={text}
        onChange={(event) => {
          setText(event.target.value);
          onValueChange(event.target.value.split(/\s+/).filter(Boolean));
        }}
      />
    );
  },
}));

let container: HTMLDivElement;
let root: Root;
const player = (
  name: string,
  extensions = [".mp4"],
): BakabaseAbstractionsModelsDomainMediaLibraryPlayer => ({
  executablePath: `/apps/${name}`,
  command: "{0}",
  extensions,
});
const button = (text: string, scope: ParentNode = container) =>
  Array.from(scope.querySelectorAll("button")).find((node) => node.textContent === text)!;
const input = (label: string, scope: ParentNode = container) =>
  Array.from(scope.querySelectorAll<HTMLInputElement>("input")).find(
    (node) => node.getAttribute("aria-label") === label,
  )!;
const click = (text: string, scope: ParentNode = container) =>
  act(() => button(text, scope).click());
const change = (target: HTMLInputElement, value: string) =>
  act(() => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(target, value);
    target.dispatchEvent(new Event("input", { bubbles: true }));
  });
const pendingSave = () => {
  let resolve!: () => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<void>((done, fail) => {
    resolve = done;
    reject = fail;
  });

  return { promise, resolve, reject };
};

beforeEach(() => {
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe("playback configuration editors", () => {
  it.each(["player", "playable"])(
    "%s waits for save, keeps the draft after failure and closes only after a successful retry",
    async (kind) => {
      const pending = pendingSave();
      const onSubmit = vi
        .fn()
        .mockReturnValueOnce(pending.promise)
        .mockResolvedValueOnce(undefined);

      act(() =>
        root.render(
          kind === "player" ? (
            <PlayerSelectorModal players={[player("test-player")]} onSubmit={onSubmit} />
          ) : (
            <PlayableFileSelectorModal options={{ extensions: [".mp4"] }} onSubmit={onSubmit} />
          ),
        ),
      );
      click("common.action.save");
      expect(container.querySelector('[role="dialog"]')).not.toBeNull();
      expect(button("common.action.cancel").disabled).toBe(true);
      click("common.action.save");
      expect(onSubmit).toHaveBeenCalledTimes(1);
      await act(async () => {
        pending.reject(new Error("Saved configuration was rejected"));
      });
      expect(container.querySelector('[role="alert"]')?.textContent).toBe(
        "Saved configuration was rejected",
      );
      expect(container.querySelector('[role="dialog"]')).not.toBeNull();
      expect(
        input(
          kind === "player"
            ? "resourceProfile.players.openTheseFiles"
            : "resourceProfile.label.fileExtensions",
        ).value,
      ).toBe(".mp4");
      await act(async () => {
        button("common.action.save").click();
      });
      expect(onSubmit).toHaveBeenCalledTimes(2);
      expect(container.querySelector('[role="dialog"]')).toBeNull();
    },
  );

  it("validates selected player files and excludes editor-only preview fields from saving", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    act(() => root.render(<PlayerSelectorModal onSubmit={onSubmit} />));
    click("resourceProfile.action.addPlayer");
    expect(button("common.action.save").disabled).toBe(true);
    const executable = input("resourceProfile.label.executablePath");

    change(executable, "unresolved");
    expect(button("common.action.save").disabled).toBe(true);
    change(executable, "/apps/");
    expect(button("common.action.save").disabled).toBe(true);
    change(executable, "/apps/player");
    change(input("resourceProfile.label.testFilePath"), "/media/test.mp4");
    change(input("resourceProfile.players.openTheseFiles"), "MP4 .mp4 .MKV");
    await act(async () => {
      button("common.action.save").click();
    });
    expect(onSubmit).toHaveBeenCalledWith([
      { executablePath: "/apps/player", command: "{0}", extensions: [".mp4", ".mkv"] },
    ]);
  });

  it("keeps the surviving player's extension draft attached to the correct row after deletion", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    act(() =>
      root.render(
        <PlayerSelectorModal
          players={[player("first", [".mp4"]), player("second", [".flac"])]}
          onSubmit={onSubmit}
        />,
      ),
    );
    const second = container.querySelector('section[aria-label="second"]')!;

    change(input("resourceProfile.players.openTheseFiles", second), ".wav");
    click(
      "resourceProfile.players.remove",
      container.querySelector('section[aria-label="first"]')!,
    );
    expect(input("resourceProfile.players.openTheseFiles").value).toBe(".wav");
    await act(async () => {
      button("common.action.save").click();
    });
    expect(onSubmit).toHaveBeenCalledWith([
      { executablePath: "/apps/second", command: "{0}", extensions: [".wav"] },
    ]);
  });

  it("merges playable presets in dotted form without replacing custom types or duplicating case variants", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    act(() =>
      root.render(
        <PlayableFileSelectorModal
          options={{ extensions: ["MP4", ".MP4", ".custom"], fileNamePattern: "^main" }}
          onSubmit={onSubmit}
        />,
      ),
    );
    click("resourceProfile.label.video");
    const field = input("resourceProfile.label.fileExtensions");

    change(field, `${field.value} .own`);
    expect(input("resourceProfile.label.fileExtensions")).toBe(field);
    await act(async () => {
      button("common.action.save").click();
    });
    const saved = onSubmit.mock.calls[0][0];

    expect(saved.fileNamePattern).toBe("^main");
    expect(saved.extensions.filter((extension: string) => extension === ".mp4")).toHaveLength(1);
    expect(saved.extensions).toContain(".custom");
    expect(saved.extensions).toContain(".own");
    expect(saved.extensions.every((extension: string) => extension.startsWith("."))).toBe(true);
  });

  it("requires extensions when a file name filter is set and permits clearing both to remove the configuration", async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    act(() =>
      root.render(
        <PlayableFileSelectorModal
          options={{ extensions: [".mp4"], fileNamePattern: "^main" }}
          onSubmit={onSubmit}
        />,
      ),
    );
    click("resourceProfile.action.clearAll");
    expect(button("common.action.save").disabled).toBe(true);
    expect(container.textContent).toContain("resourceProfile.playable.patternNeedsExtensions");
    change(input("resourceProfile.label.fileNamePattern"), "");
    await act(async () => {
      button("common.action.save").click();
    });
    expect(onSubmit).toHaveBeenCalledWith({ extensions: [], fileNamePattern: undefined });
  });

  it("cancel closes without submitting draft changes", () => {
    const onSubmit = vi.fn();

    act(() => root.render(<PlayableFileSelectorModal onSubmit={onSubmit} />));
    click("resourceProfile.label.audio");
    click("common.action.cancel");
    expect(onSubmit).not.toHaveBeenCalled();
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });
});

describe("player command preview", () => {
  it("matches backend argument quoting for spaces, existing quotes and numbered placeholders", () => {
    expect(generatePlayerCommand("/apps/player", "--full {0}", "/media/a b.mp4")).toBe(
      '"/apps/player" --full "/media/a b.mp4"',
    );
    expect(
      generatePlayerCommand("/apps/player", "--one \"{0}\" --two '{1}'", '/media/a"b.mp4'),
    ).toBe('"/apps/player" --one "/media/a\\"b.mp4" --two \'/media/a\\"b.mp4\'');
    expect(generatePlayerCommand("/apps/player", "", "/media/file.mp4")).toBe(
      '"/apps/player" "/media/file.mp4"',
    );
  });
});
