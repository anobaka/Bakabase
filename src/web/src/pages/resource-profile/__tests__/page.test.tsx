import type { BakabaseServiceModelsViewResourceProfileViewModel as ResourceProfile } from "@/sdk/Api";

import { HeroUIProvider } from "@heroui/react";
import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceProfilePage from "..";
import PropertyPoolModal from "../components/PropertyPoolModal";
import EnhancementConfigPanel from "../components/EnhancementConfigPanel";
import DisplayNameTemplateEditorModal from "../components/DisplayNameTemplateEditorModal";
import DeleteEnhancementsModal from "../components/DeleteEnhancementsModal";
import ResourceProfileTestModal from "../components/ResourceProfileTestModal";
import ResourceProfileModal from "../components/ResourceProfileModal";

import { PropertyPool, PropertyValueScope } from "@/sdk/constants";

const api = vi.hoisted(() => ({
  profiles: vi.fn(),
  properties: vi.fn(),
  enhancers: vi.fn(),
  update: vi.fn(),
  add: vi.fn(),
  remove: vi.fn(),
  portal: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    resourceProfile: {
      getAllResourceProfiles: api.profiles,
      updateResourceProfile: api.update,
      addResourceProfile: api.add,
      deleteResourceProfile: api.remove,
    },
    property: { getPropertiesByPool: api.properties },
    enhancer: { getAllEnhancerDescriptors: api.enhancers },
  },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: api.portal }),
}));
vi.mock("@/components/HelpCenter", () => ({ HelpCenterButton: () => null }));
vi.mock("@/components/Property", () => ({
  PropertyLabel: ({ property }: any) => <span>{property.name}</span>,
}));
vi.mock("@/components/Chips/Enhancer/BriefEnhancer", () => ({ default: () => null }));
vi.mock("@/components/ConfirmModal", () => ({ default: () => null }));
vi.mock("../components/PropertyPoolModal", () => ({ default: () => null }));
vi.mock("../components/EnhancementConfigPanel", () => ({ default: () => null }));
vi.mock("../components/DisplayNameTemplateEditorModal", () => ({ default: () => null }));
vi.mock("../components/PlayableFileSelectorModal", () => ({ default: () => null }));
vi.mock("../components/PlayerSelectorModal", () => ({ default: () => null }));
vi.mock("../components/DeleteEnhancementsModal", () => ({ default: () => null }));
vi.mock("../components/ResourceProfileTestModal", () => ({ default: () => null }));
vi.mock("../components/ResourceProfileModal", () => ({ default: () => null }));
vi.mock("@/components/ResourceFilter", () => ({
  // The filter editor has its own tests; exercise the page's draft/save boundary.
  ResourceFilterController: ({ group, isReadonly, onGroupChange }: any) => (
    <div data-testid="scope-filter" data-readonly={String(isReadonly)}>
      <output>{JSON.stringify(group)}</output>
      {!isReadonly && (
        <button
          onClick={() =>
            onGroupChange({
              combinator: 1,
              disabled: false,
              filters: [
                {
                  propertyPool: 4,
                  propertyId: 9,
                  operation: 1,
                  dbValue: '"Draft"',
                  disabled: false,
                },
              ],
            })
          }
        >
          Change scope filter
        </button>
      )}
    </div>
  ),
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Popover: (await import("@/components/bakaui/components/Popover")).default,
}));

const profile = (id: number, name: string, priority: number): ResourceProfile => ({
  id,
  name,
  priority,
  createdAt: "2026-09-15T00:00:00Z",
  updatedAt: "2026-09-15T00:00:00Z",
});
const configured: ResourceProfile = {
  ...profile(1, "Films", 20),
  search: {
    page: 1,
    pageSize: 100,
    group: {
      combinator: 1,
      disabled: false,
      filters: [
        { propertyPool: 4, propertyId: 9, operation: 1, dbValue: '"Original"', disabled: false },
      ],
    },
  },
  nameTemplate: "{Name}",
  propertyOptions: {
    properties: [
      {
        pool: PropertyPool.Custom,
        id: 9,
        scopePriority: [PropertyValueScope.Synchronization, PropertyValueScope.Manual],
      },
    ],
  },
  playerOptions: {
    players: [{ executablePath: "/Applications/Player", extensions: [".mp4"], command: "{0}" }],
  },
  playableFileOptions: { extensions: [".mp4"], fileNamePattern: "episode" },
  enhancerOptions: {
    enhancers: [
      {
        enhancerId: 7,
        targetOptions: [{ propertyPool: PropertyPool.Custom, propertyId: 9, target: 1 }],
      },
    ],
  },
};
let host: HTMLDivElement;
let root: Root;
function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}
async function render() {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <ResourceProfilePage />
      </HeroUIProvider>,
    ),
  );
}
async function click(element: Element) {
  await act(async () => (element as HTMLElement).click());
}
function button(label: string) {
  const found = [...document.querySelectorAll<HTMLButtonElement>("button")].find(
    (el) => (el.getAttribute("aria-label") || el.textContent?.trim()) === label,
  );
  if (!found) throw new Error(`Missing button ${label}`);
  return found;
}
function profileButton(name: string) {
  const found = [...host.querySelectorAll<HTMLButtonElement>("aside button[aria-pressed]")].find(
    (el) => el.firstElementChild?.textContent === name,
  );
  if (!found) throw new Error(`Missing profile ${name}`);
  return found;
}
async function search(value: string) {
  const input = host.querySelector<HTMLInputElement>(
    '[aria-label="resourceProfile.input.search"]',
  )!;
  await act(async () => {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
function portalProps(component: unknown): any {
  const call = [...api.portal.mock.calls].reverse().find(([type]) => type === component);
  expect(call).toBeDefined();
  return call![1];
}

beforeEach(() => {
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  Object.values(api).forEach((mock) => mock.mockReset());
  api.profiles.mockResolvedValue({
    code: 0,
    data: [profile(2, "Books", 5), structuredClone(configured)],
  });
  api.properties.mockResolvedValue({
    code: 0,
    data: [{ id: 9, pool: PropertyPool.Custom, name: "Author" }],
  });
  api.enhancers.mockResolvedValue({ code: 0, data: [] });
  api.update.mockResolvedValue({ code: 0 });
  host = document.createElement("div");
  document.body.append(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
});

describe("resource profile page editing", () => {
  it("selects the highest priority, filters names without changing the selected profile, and preserves selection after reorder", async () => {
    await render();
    expect(profileButton("Films")).toHaveAttribute("aria-pressed", "true");
    await search("  BOOK  ");
    expect(host.querySelectorAll("aside button[aria-pressed]")).toHaveLength(1);
    await click(profileButton("Books"));
    await click(button("resourceProfile.action.editBasicInfo"));
    const props = portalProps(ResourceProfileModal);
    await act(async () => props.onUpdate(2, { priority: 30, name: "Books updated" }));
    expect(profileButton("Books updated")).toHaveAttribute("aria-pressed", "true");
    await search("");
    expect(host.querySelector("aside button[aria-pressed]")?.firstElementChild?.textContent).toBe(
      "Books updated",
    );
  });

  it("cancels scope changes without a write, and saving replaces only criteria while preserving all configuration", async () => {
    await render();
    await click(button("resourceProfile.action.editScope"));
    expect(profileButton("Books")).toBeDisabled();
    await click(button("Change scope filter"));
    await click(button("resourceProfile.action.testCriteria"));
    expect(portalProps(ResourceProfileTestModal)).toMatchObject({
      isDraft: true,
      profile: { search: { group: { filters: [{ dbValue: '"Draft"' }] } } },
    });
    await click(button("common.action.cancel"));
    expect(api.update).not.toHaveBeenCalled();
    expect(host.querySelector('[data-testid="scope-filter"]')?.textContent).toContain("Original");
    expect(profileButton("Books")).toBeEnabled();

    await click(button("resourceProfile.action.editScope"));
    await click(button("Change scope filter"));
    await click(button("common.action.save"));
    expect(api.update).toHaveBeenCalledTimes(1);
    const [id, input] = api.update.mock.calls[0];
    expect(id).toBe(1);
    expect(input).toMatchObject({
      name: configured.name,
      priority: configured.priority,
      nameTemplate: configured.nameTemplate,
      propertyOptions: configured.propertyOptions,
      enhancerOptions: configured.enhancerOptions,
      playerOptions: configured.playerOptions,
      playableFileOptions: configured.playableFileOptions,
      search: { group: { filters: [{ dbValue: '"Draft"' }] } },
    });
    expect(host.querySelector('[data-testid="scope-filter"]')).toHaveAttribute(
      "data-readonly",
      "true",
    );
  });

  it("retains the draft after a rejected save and retries the same changes", async () => {
    api.update.mockResolvedValueOnce({ code: 400, message: "Unable to save" });
    await render();
    await click(button("resourceProfile.action.editScope"));
    await click(button("Change scope filter"));
    await click(button("common.action.save"));
    expect(host.querySelector('[role="alert"]')).toHaveTextContent("Unable to save");
    expect(host.querySelector('[data-testid="scope-filter"]')).toHaveAttribute(
      "data-readonly",
      "false",
    );
    expect(host.querySelector('[data-testid="scope-filter"]')).toHaveTextContent("Draft");
    expect(profileButton("Books")).toBeDisabled();
    await click(button("common.action.save"));
    expect(api.update).toHaveBeenCalledTimes(2);
    expect(api.update.mock.calls[1]).toEqual(api.update.mock.calls[0]);
    expect(host.querySelector('[role="alert"]')).toBeNull();
  });

  it("returns the save Promise to modal editors and merges later edits with the latest profile", async () => {
    await render();
    await click(button("resourceProfile.action.configureName"));
    const titleEditor = portalProps(DisplayNameTemplateEditorModal);
    await click(button("resourceProfile.action.configureProperties"));
    const propertyEditor = portalProps(PropertyPoolModal);
    const pending = deferred<{ code: number }>();
    api.update.mockReturnValueOnce(pending.promise);
    let save!: Promise<void>;
    await act(async () => {
      save = titleEditor.onSubmit("Updated title");
    });
    expect(save).toBeInstanceOf(Promise);
    expect(button("resourceProfile.action.configureProperties")).toBeDisabled();
    await act(async () => {
      pending.resolve({ code: 0 });
      await save;
    });
    const properties = {
      properties: [
        { pool: PropertyPool.Custom, id: 9, scopePriority: [PropertyValueScope.Manual] },
      ],
    };
    await act(async () => propertyEditor.onSubmit(properties));
    expect(api.update.mock.calls[1][1]).toMatchObject({
      nameTemplate: "Updated title",
      propertyOptions: properties,
    });

    api.update.mockResolvedValueOnce({ code: 409, message: "Conflict" });
    await act(async () => {
      await expect(titleEditor.onSubmit("Rejected title")).rejects.toThrow("Conflict");
    });
    await act(async () => propertyEditor.onSubmit(configured.propertyOptions));
    expect(api.update.mock.calls[api.update.mock.calls.length - 1][1].nameTemplate).toBe(
      "Updated title",
    );
  });

  it("merges enhancer targets by pool and id without overwriting scope priorities or adding duplicates", async () => {
    await render();
    await click(button("resourceProfile.action.configureEnhancers"));
    const options = [
      {
        enhancerId: 8,
        targetOptions: [
          { target: 1, propertyPool: PropertyPool.Custom, propertyId: 9 },
          { target: 2, propertyPool: PropertyPool.Reserved, propertyId: 9 },
          { target: 3, propertyPool: PropertyPool.Custom, propertyId: 10 },
          { target: 4, propertyPool: PropertyPool.Custom, propertyId: 10 },
          { target: 5 },
        ],
      },
    ];
    await act(async () => portalProps(EnhancementConfigPanel).onSubmit(options));
    const input = api.update.mock.calls[0][1];
    expect(input.enhancerOptions).toEqual({ enhancers: options });
    expect(input.propertyOptions.properties).toEqual([
      configured.propertyOptions!.properties![0],
      { pool: PropertyPool.Reserved, id: 9 },
      { pool: PropertyPool.Custom, id: 10 },
    ]);
    expect(input.playableFileOptions).toEqual(configured.playableFileOptions);
    await act(async () => portalProps(EnhancementConfigPanel).onSubmit([]));
    expect(api.update.mock.calls[1][1].enhancerOptions).toBeNull();
    expect(api.update.mock.calls[1][1].propertyOptions).toEqual(input.propertyOptions);
  });

  it("disables metadata dependent actions until metadata succeeds and supports retry", async () => {
    const pending = deferred<{ code: number; data: unknown[] }>();
    api.enhancers.mockReturnValueOnce(pending.promise);
    await render();
    expect(button("resourceProfile.action.configureProperties")).toBeDisabled();
    expect(button("resourceProfile.action.configureName")).toBeDisabled();
    await act(async () => pending.reject(new Error("Metadata unavailable")));
    expect(host.querySelector('[role="alert"]')).toHaveTextContent("Metadata unavailable");
    expect(button("resourceProfile.action.configureEnhancers")).toBeDisabled();
    await click(button("resourceProfile.action.retry"));
    expect(button("resourceProfile.action.configureProperties")).toBeEnabled();
    expect(button("resourceProfile.action.configureEnhancers")).toBeEnabled();
  });

  it("passes the current full profile to the delete enhancements dialog without deleting the profile", async () => {
    await render();
    await click(button("resourceProfile.label.more"));
    const item = [...document.querySelectorAll('[role="menuitem"], [role="option"]')].find(
      (el) => el.textContent?.trim() === "resourceProfile.action.deleteEnhancements",
    );
    expect(item).toBeDefined();
    await click(item!);
    expect(portalProps(DeleteEnhancementsModal)).toEqual({ profile: configured });
    expect(api.remove).not.toHaveBeenCalled();
    expect(api.update).not.toHaveBeenCalled();
  });
});
