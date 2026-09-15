import type { ReactNode } from "react";
import type { IProperty } from "@/components/Property/models";

import { forwardRef } from "react";
import { createRoot, type Root } from "react-dom/client";
import { act } from "react-dom/test-utils";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DisplayNameTemplateEditorModal from "../DisplayNameTemplateEditorModal";

import { PropertyPool, PropertyType, WellKnownTextType } from "@/sdk/constants";

const { getAllTextTypes, getTextEntries } = vi.hoisted(() => ({
  getAllTextTypes: vi.fn(),
  getTextEntries: vi.fn(),
}));

vi.mock("@/sdk/BApi.tsx", () => ({ default: { text: { getAllTextTypes, getTextEntries } } }));
vi.mock("@/i18n", () => ({ getEnumKey: (type: string, key: string) => `${type}.${key}` }));
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
  Chip: ({ children, color }: any) => <span data-color={color}>{children}</span>,
  Spinner: () => <span>loading</span>,
  Button: ({
    children,
    onPress,
    isDisabled,
    isLoading,
    "aria-label": label,
    "aria-pressed": pressed,
  }: any) => (
    <button
      aria-label={label}
      aria-pressed={pressed}
      disabled={isDisabled || isLoading}
      onClick={() => void onPress?.()}
    >
      {children}
    </button>
  ),
  Input: ({ value, onValueChange, "aria-label": label }: any) => (
    <input
      aria-label={label}
      value={value}
      onChange={(event) => onValueChange(event.target.value)}
    />
  ),
  Textarea: forwardRef<HTMLTextAreaElement, any>(
    ({ value, onValueChange, label, isDisabled }, ref) => (
      <textarea
        ref={ref}
        aria-label={label}
        disabled={isDisabled}
        value={value}
        onChange={(event) => onValueChange(event.target.value)}
      />
    ),
  ),
}));

let container: HTMLDivElement;
let root: Root;
const properties: Omit<IProperty, "bizValueType" | "dbValueType">[] = [
  {
    id: 11,
    name: "Artist",
    pool: PropertyPool.Custom,
    type: PropertyType.SingleLineText,
    typeName: "Text",
    poolName: "Custom",
    order: 0,
  },
  {
    id: 12,
    name: "Studio",
    pool: PropertyPool.Custom,
    type: PropertyType.Tags,
    typeName: "Tags",
    poolName: "Custom",
    order: 1,
  },
];
const button = (text: string) =>
  Array.from(container.querySelectorAll("button")).find((node) => node.textContent === text);
const textarea = () => container.querySelector("textarea")!;
const change = (element: HTMLInputElement | HTMLTextAreaElement, value: string) =>
  act(() => {
    const prototype =
      element.tagName === "TEXTAREA" ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;

    Object.getOwnPropertyDescriptor(prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });

beforeEach(() => {
  (globalThis as any).IS_REACT_ACT_ENVIRONMENT = true;
  getAllTextTypes
    .mockReset()
    .mockResolvedValue({ code: 0, data: [{ id: 21, wellKnown: WellKnownTextType.Wrapper }] });
  getTextEntries.mockReset().mockResolvedValue({ code: 0, data: [{ value1: "[", value2: "]" }] });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
});
afterEach(() => {
  act(() => root.unmount());
  container.remove();
});

describe("name template editor", () => {
  it("searches and filters properties locally while preserving keyboard-operable insertion buttons", async () => {
    await act(async () => {
      root.render(<DisplayNameTemplateEditorModal properties={properties} />);
    });
    expect(button("Artist")?.tagName).toBe("BUTTON");
    expect(button("Studio")?.tagName).toBe("BUTTON");
    const search = container.querySelector<HTMLInputElement>(
      'input[aria-label="resourceProfile.nameEditor.searchProperties"]',
    )!;

    change(search, "ART");
    expect(button("Artist")).toBeDefined();
    expect(button("Studio")).toBeUndefined();
    change(search, "");
    act(() => button("PropertyType.Tags")!.click());
    expect(button("Artist")).toBeUndefined();
    expect(button("Studio")).toBeDefined();
    expect(button("PropertyType.Tags")?.getAttribute("aria-pressed")).toBe("true");
    expect(getAllTextTypes).toHaveBeenCalledTimes(1);
    expect(getTextEntries).toHaveBeenCalledWith(21);
  });

  it("inserts at the current selection, wraps selected text and retains syntax-only highlighting", async () => {
    await act(async () => {
      root.render(<DisplayNameTemplateEditorModal properties={properties} template="A B" />);
    });
    textarea().setSelectionRange(2, 2);
    act(() => button("Artist")!.click());
    expect(textarea().value).toBe("A {Artist}B");
    textarea().setSelectionRange(0, 1);
    const wrapper = Array.from(container.querySelectorAll("button")).find(
      (node) => node.textContent?.replace(/\s/g, "") === "[]",
    )!;

    act(() => wrapper.click());
    expect(textarea().value).toBe("[A] {Artist}B");
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
    expect(document.activeElement).toBe(textarea());
    expect(textarea().selectionStart).toBe(2);
    change(textarea(), "[{Artist}] {Removed}");
    const preview = container.querySelector(
      '[aria-label="resourceProfile.nameEditor.previewTitle"]',
    )!;

    expect(preview.querySelector('[data-color="primary"]')?.textContent).toBe("Artist");
    expect(preview.querySelector('[data-color="danger"]')?.textContent).toBe("Removed");
    expect(preview.querySelector('[data-color="secondary"]')?.textContent).toBe("[");
    expect(container.textContent).toContain("resourceProfile.nameEditor.previewHint");
  });

  it("keeps properties and drafts available when wrapper loading fails, and allows a retry", async () => {
    getAllTextTypes.mockRejectedValueOnce(new Error("Wrapper service unavailable"));
    await act(async () => {
      root.render(<DisplayNameTemplateEditorModal properties={properties} template="Draft" />);
    });
    expect(container.querySelector('[role="alert"]')?.textContent).toContain(
      "Wrapper service unavailable",
    );
    expect(button("Artist")).toBeDefined();
    expect(textarea().value).toBe("Draft");
    await act(async () => {
      button("common.action.retry")!.click();
    });
    expect(getAllTextTypes).toHaveBeenCalledTimes(2);
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(textarea().value).toBe("Draft");
  });

  it("preserves a failed save and can clear the template to restore fallback behavior", async () => {
    const onSubmit = vi
      .fn()
      .mockRejectedValueOnce(new Error("Save failed"))
      .mockResolvedValueOnce(undefined);

    await act(async () => {
      root.render(
        <DisplayNameTemplateEditorModal
          properties={properties}
          template="{Artist}"
          onSubmit={onSubmit}
        />,
      );
    });
    await act(async () => {
      button("common.action.save")!.click();
    });
    expect(container.querySelector('[role="alert"]')?.textContent).toBe("Save failed");
    expect(textarea().value).toBe("{Artist}");
    change(textarea(), "");
    await act(async () => {
      button("common.action.save")!.click();
    });
    expect(onSubmit).toHaveBeenLastCalledWith("");
    expect(container.querySelector('[role="dialog"]')).toBeNull();
  });
});
