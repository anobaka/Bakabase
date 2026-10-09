import { afterEach, describe, expect, it, vi } from "vitest";

import {
  deleteShortcutLabel,
  isDeleteShortcut,
  isPrimaryModifierPressed,
  matchesPrimaryShortcut,
  primaryModifierKey,
  primaryShortcutLabel,
  shouldIgnoreGlobalShortcut,
  usesAppleKeys,
} from "../keyboard";

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.innerHTML = "";
});

describe("browser keyboard conventions", () => {
  it.each([
    ["MacIntel", "Meta", "⌘+A", "⌘+⌫", "metaKey", "ctrlKey"],
    ["Win32", "Control", "Ctrl+A", "Delete", "ctrlKey", "metaKey"],
    ["Linux x86_64", "Control", "Ctrl+A", "Delete", "ctrlKey", "metaKey"],
  ])(
    "uses %s browser keys for both labels and matching",
    (platform, key, select, remove, primary, other) => {
      vi.stubGlobal("navigator", { platform });
      expect(primaryModifierKey()).toBe(key);
      expect(primaryShortcutLabel("A")).toBe(select);
      expect(deleteShortcutLabel()).toBe(remove);
      expect(
        matchesPrimaryShortcut(new KeyboardEvent("keydown", { key: "A", [primary]: true }), "a"),
      ).toBe(true);
      expect(
        matchesPrimaryShortcut(new KeyboardEvent("keydown", { key: "a", [other]: true }), "a"),
      ).toBe(false);
      expect(isPrimaryModifierPressed(new MouseEvent("click", { [primary]: true }))).toBe(true);
      expect(isPrimaryModifierPressed(new MouseEvent("click", { [other]: true }))).toBe(false);
      expect(isDeleteShortcut(new KeyboardEvent("keydown", { key: "Backspace" }))).toBe(false);
      expect(
        isDeleteShortcut(new KeyboardEvent("keydown", { key: "Backspace", metaKey: true })),
      ).toBe(platform === "MacIntel");
    },
  );

  it("uses browser platform hints with safe fallbacks, including Apple hardware keyboards", () => {
    expect(usesAppleKeys({ platform: "MacIntel", userAgentData: { platform: "Windows" } })).toBe(
      false,
    );
    expect(usesAppleKeys({ platform: "Win32", userAgentData: { platform: "macOS" } })).toBe(true);
    expect(usesAppleKeys({ platform: "iPad" })).toBe(true);
    expect(usesAppleKeys({ userAgent: "Mozilla/5.0 (Macintosh; Intel Mac OS X)" })).toBe(true);
    expect(usesAppleKeys({})).toBe(false);
  });

  it("does not match extra modifiers, composition, repeat or an already handled shortcut", () => {
    vi.stubGlobal("navigator", { platform: "MacIntel" });
    for (const extra of [
      { altKey: true },
      { shiftKey: true },
      { ctrlKey: true },
      { repeat: true },
      { isComposing: true },
    ]) {
      expect(
        matchesPrimaryShortcut(
          new KeyboardEvent("keydown", { key: "c", metaKey: true, ...extra }),
          "c",
        ),
      ).toBe(false);
    }
    const handled = new KeyboardEvent("keydown", { key: "c", metaKey: true, cancelable: true });

    handled.preventDefault();
    expect(matchesPrimaryShortcut(handled, "c")).toBe(false);
  });

  it.each([
    "<input>",
    "<textarea></textarea>",
    "<select></select>",
    "<div contenteditable><span>edit</span></div>",
    "<div contenteditable='plaintext-only'><span>edit</span></div>",
    "<div role='dialog'><span>dialog</span></div>",
    "<div role='menuitem'>action</div>",
  ])("leaves local keyboard behavior to %s", (html) => {
    document.body.innerHTML = html;
    const target = document.body.querySelector("span") ?? document.body.firstElementChild!;
    const event = new KeyboardEvent("keydown", { key: "Delete", bubbles: true });

    target.dispatchEvent(event);
    expect(shouldIgnoreGlobalShortcut(event)).toBe(true);
  });
});
