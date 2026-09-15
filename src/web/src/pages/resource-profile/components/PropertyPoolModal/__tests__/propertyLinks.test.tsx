import type { ReactNode } from "react";
import type { ModalProps } from "@/components/bakaui/components/Modal";
import type { IProperty } from "@/components/Property/models";
import type { BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions as PropertyOptions } from "@/sdk/Api";

import { useState } from "react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PropertyPoolModal from "../index";
import ScopePriorityEditor from "../../ScopePriorityEditor";
import GlobalScopePriorityModal from "../../GlobalScopePriorityModal";

import SharedModal from "@/components/bakaui/components/Modal";
import { PropertyPool, PropertyType, PropertyValueScope } from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  portal: vi.fn(),
  patch: vi.fn(),
  storePatch: vi.fn(),
  update: vi.fn(),
  toast: vi.fn(),
}));

vi.mock("@/sdk/BApi", () => ({
  default: { options: { patchResourceOptions: mocks.patch } },
}));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: object) => (values ? `${key}:${JSON.stringify(values)}` : key),
  }),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Modal: (props: ModalProps) => <SharedModal disableAnimation {...props} />,
  toast: { danger: mocks.toast },
}));
vi.mock("@/components/Property", () => ({
  PropertyLabel: ({ property }: { property: IProperty }) => (
    <span data-standard-property={property.id}>{property.name}</span>
  ),
}));
vi.mock("@/components/PropertySelector", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/stores/options", () => ({
  useResourceOptionsStore: (selector?: (store: unknown) => unknown) => {
    const store = {
      data: {
        propertyValueScopePriority: [PropertyValueScope.Synchronization, PropertyValueScope.Manual],
      },
      patch: mocks.storePatch,
      update: mocks.update,
    };

    return selector ? selector(store) : store;
  },
}));

let root: Root;
let host: HTMLDivElement;
const Manual = PropertyValueScope.Manual;
const Sync = PropertyValueScope.Synchronization;
const unknownScope = 9999 as PropertyValueScope;

function property(id: number, name: string, type = PropertyType.SingleLineText) {
  return { id, name, type, pool: PropertyPool.Custom } as IProperty;
}
const properties = [
  property(1, "Author"),
  property(2, "Topic", PropertyType.Tags),
  property(3, "Rating", PropertyType.Rating),
];
const initial: PropertyOptions = {
  properties: [
    { pool: PropertyPool.Custom, id: 1, scopePriority: [Sync, Manual] },
    { pool: PropertyPool.Custom, id: 999, scopePriority: [unknownScope, Manual] },
  ],
};

function deferred<T = void>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });

  return { promise, resolve, reject };
}
async function render(content: ReactNode) {
  await act(async () => root.render(<HeroUIProvider disableAnimation>{content}</HeroUIProvider>));
}
async function click(element: Element) {
  await act(async () => (element as HTMLElement).click());
}
function button(label: string) {
  return [...document.querySelectorAll("button")].find((el) => el.textContent === label)!;
}
function labelledButton(prefix: string) {
  return [...document.querySelectorAll("button")].find((el) =>
    el.getAttribute("aria-label")?.startsWith(prefix),
  )!;
}
function props(onSubmit = vi.fn(), propertyOptions: PropertyOptions | undefined = initial) {
  return {
    allProperties: properties,
    enhancerOptions: [],
    enhancerDescriptors: [],
    propertyOptions,
    onSubmit,
  };
}
async function search(value: string) {
  await act(async () => {
    const input = document.querySelector<HTMLInputElement>(
      '[aria-label="resourceProfile.propertyPool.search"]',
    )!;

    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.patch.mockReset();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("linked profile properties", () => {
  it("retains existing scope priorities and missing references when selecting more properties", async () => {
    const submit = vi.fn();

    await render(<PropertyPoolModal {...props(submit)} />);
    await click(button("resourceProfile.propertyPool.addProperty"));
    const selection = mocks.portal.mock.lastCall![1];

    expect(selection.selection).toEqual([{ pool: PropertyPool.Custom, id: 1 }]);
    await act(async () => selection.onSubmit([properties[0], properties[1], properties[1]]));
    await click(button("common.action.save"));
    expect(submit).toHaveBeenCalledWith({
      properties: [
        { pool: PropertyPool.Custom, id: 1, scopePriority: [Sync, Manual] },
        { pool: PropertyPool.Custom, id: 999, scopePriority: [unknownScope, Manual] },
        { pool: PropertyPool.Custom, id: 2 },
      ],
    });
    expect(initial.properties![0].scopePriority).toEqual([Sync, Manual]);
  });

  it("searches names, translated types and missing IDs without changing associations", async () => {
    const submit = vi.fn();
    const options: PropertyOptions = {
      properties: [...initial.properties!, { pool: PropertyPool.Custom, id: 2 }],
    };

    await render(<PropertyPoolModal {...props(submit, options)} />);
    expect(document.querySelectorAll("[data-standard-property]")).toHaveLength(2);
    await search("tags");
    expect(document.querySelectorAll('[role="listitem"]')).toHaveLength(1);
    expect(document.querySelector('[role="listitem"]')).toHaveTextContent("Topic");
    expect(document.querySelector('[role="listitem"]')).toHaveTextContent("PropertyType.Tags");
    await search("999");
    expect(document.querySelector('[role="listitem"]')).toHaveTextContent(
      "resourceProfile.propertyPool.unknownProperty",
    );
    expect(document.querySelector('[role="listitem"]')).toHaveTextContent('"id":999');
    await search("no match");
    expect(document.body).toHaveTextContent("resourceProfile.propertyPool.noMatches");
    await click(button("common.action.save"));
    expect(submit).toHaveBeenCalledWith(options);
  });

  it("only unlinks missing refs explicitly and returns inherited settings when every link is removed", async () => {
    const submit = vi.fn();

    await render(
      <PropertyPoolModal {...props(submit, { properties: [initial.properties![1]] })} />,
    );
    expect(document.body).toHaveTextContent("resourceProfile.propertyPool.unlinkHint");
    await click(labelledButton("resourceProfile.propertyPool.unlinkNamed"));
    expect(document.body).toHaveTextContent("resourceProfile.propertyPool.empty");
    await click(button("common.action.save"));
    expect(submit).toHaveBeenCalledWith(undefined);
    expect(mocks.patch).not.toHaveBeenCalled();
  });

  it("waits for saving, keeps the dialog open on failure, and allows retry", async () => {
    const pending = deferred();
    const submit = vi.fn().mockReturnValueOnce(pending.promise).mockResolvedValueOnce(undefined);
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});

    await render(<PropertyPoolModal {...props(submit)} />);
    await click(button("common.action.save"));
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(button("common.action.cancel")).toBeDisabled();
    expect(button("resourceProfile.propertyPool.addProperty")).toBeDisabled();
    await act(async () => pending.reject(new Error("Save failed")));
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(mocks.toast).toHaveBeenCalled();
    expect(button("common.action.cancel")).not.toBeDisabled();
    await click(button("common.action.save"));
    expect(submit).toHaveBeenCalledTimes(2);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    errors.mockRestore();
  });
});

describe("scope priority editing", () => {
  it("starts customization from the current global order without changing it merely by opening", async () => {
    const changed = vi.fn();

    function Harness() {
      const [value, setValue] = useState<PropertyValueScope[] | null>(null);

      return (
        <ScopePriorityEditor
          availableScopes={[Sync, Manual]}
          value={value}
          onChange={(next) => {
            changed(next);
            setValue(next);
          }}
        />
      );
    }
    await render(<Harness />);
    await click(button("resourceProfile.scopePriority.customize"));
    expect(changed).not.toHaveBeenCalled();
    expect(document.querySelector("ol li")).toHaveTextContent("PropertyValueScope.Synchronization");
    await click(button("resourceProfile.scopePriority.useCustomOrder"));
    expect(changed).toHaveBeenLastCalledWith([Sync, Manual]);
    await click(button("resourceProfile.scopePriority.resetTooltip"));
    expect(changed).toHaveBeenLastCalledWith(null);
  });

  it("opens compact editing on demand and preserves unknown sources when reordering or adding a source", async () => {
    const changed = vi.fn();

    function Harness() {
      const [value, setValue] = useState<PropertyValueScope[] | null>([unknownScope, Manual]);

      return (
        <ScopePriorityEditor
          availableScopes={[Sync, Manual]}
          value={value}
          onChange={(next) => {
            changed(next);
            setValue(next);
          }}
        />
      );
    }
    await render(<Harness />);
    expect(document.querySelector("ol")).toBeNull();
    await click(button("resourceProfile.scopePriority.edit"));
    expect(document.querySelector("ol")).toHaveTextContent(
      "resourceProfile.scopePriority.unknownScope",
    );
    expect(document.querySelector("ol")).toHaveTextContent("PropertyValueScope.Manual");
    await click(labelledButton("resourceProfile.scopePriority.moveDown"));
    expect(changed).toHaveBeenLastCalledWith([Manual, unknownScope]);
    await click(button("PropertyValueScope.Synchronization"));
    expect(changed).toHaveBeenLastCalledWith([Manual, unknownScope, Sync]);
    await click(button("resourceProfile.scopePriority.resetTooltip"));
    expect(changed).toHaveBeenLastCalledWith(null);
    expect(document.body).toHaveTextContent("resourceProfile.scopePriority.useGlobal");
  });

  it("keeps global changes as a draft and closes only after the explicit save succeeds", async () => {
    const pending = deferred<{ code: number }>();

    mocks.patch.mockReturnValueOnce(pending.promise);
    await render(<GlobalScopePriorityModal />);
    expect(document.body).toHaveTextContent("resourceProfile.globalScopePriority.scopeHint");
    const before = [...document.querySelectorAll("ol li")].map((row) => row.textContent);

    await click(labelledButton("resourceProfile.scopePriority.moveDown"));
    expect(mocks.patch).not.toHaveBeenCalled();
    expect([...document.querySelectorAll("ol li")].map((row) => row.textContent)).not.toEqual(
      before,
    );
    await click(button("resourceProfile.globalScopePriority.save"));
    expect(mocks.patch).toHaveBeenCalledWith({
      propertyValueScopePriority: expect.arrayContaining([Manual, Sync]),
    });
    expect(mocks.patch.mock.calls[0][0].propertyValueScopePriority.slice(0, 2)).toEqual([
      Manual,
      Sync,
    ]);
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(button("common.action.cancel")).toBeDisabled();
    expect(mocks.update).not.toHaveBeenCalled();
    expect(mocks.storePatch).not.toHaveBeenCalled();
    await act(async () => pending.resolve({ code: 0 }));
    expect(mocks.update).toHaveBeenCalledExactlyOnceWith({
      propertyValueScopePriority: mocks.patch.mock.calls[0][0].propertyValueScopePriority,
    });
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("keeps a rejected global priority draft open without updating the store, then retries successfully", async () => {
    const errors = vi.spyOn(console, "error").mockImplementation(() => {});

    mocks.patch
      .mockResolvedValueOnce({ code: 409, message: "Priority update rejected" })
      .mockResolvedValueOnce({ code: 0 });
    await render(<GlobalScopePriorityModal />);
    await click(labelledButton("resourceProfile.scopePriority.moveDown"));
    const draft = [...document.querySelectorAll("ol li")].map((row) => row.textContent);

    await click(button("resourceProfile.globalScopePriority.save"));
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(mocks.toast).toHaveBeenCalled();
    expect(mocks.update).not.toHaveBeenCalled();
    expect(mocks.storePatch).not.toHaveBeenCalled();
    expect(button("common.action.cancel")).toBeEnabled();
    expect([...document.querySelectorAll("ol li")].map((row) => row.textContent)).toEqual(draft);

    await click(button("resourceProfile.globalScopePriority.save"));
    expect(mocks.patch).toHaveBeenCalledTimes(2);
    expect(mocks.patch.mock.calls[1]).toEqual(mocks.patch.mock.calls[0]);
    expect(mocks.update).toHaveBeenCalledExactlyOnceWith(mocks.patch.mock.calls[1][0]);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
    errors.mockRestore();
  });

  it("discards a global draft when cancelled", async () => {
    await render(<GlobalScopePriorityModal />);
    await click(labelledButton("resourceProfile.scopePriority.moveDown"));
    await click(button("common.action.cancel"));
    expect(mocks.patch).not.toHaveBeenCalled();
    expect(mocks.update).not.toHaveBeenCalled();
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });
});
