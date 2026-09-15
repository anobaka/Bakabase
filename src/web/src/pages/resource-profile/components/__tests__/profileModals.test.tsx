import type { ReactNode } from "react";
import type { ModalProps } from "@/components/bakaui/components/Modal";
import type { BakabaseServiceModelsViewResourceProfileViewModel as Profile } from "@/sdk/Api";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceProfileModal from "../ResourceProfileModal";
import ResourceProfileTestModal from "../ResourceProfileTestModal";
import { hasProfileConditions } from "../../profileUtils";

import SharedModal from "@/components/bakaui/components/Modal";
import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import {
  InternalProperty,
  PropertyPool,
  PropertyType,
  PropertyValueScope,
  ReservedProperty,
  ResourceAdditionalItem,
  ResourceTag,
  SearchCombinator,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({
  create: vi.fn(),
  search: vi.fn(),
  portal: vi.fn(),
  toast: vi.fn(),
  scopePriority: undefined as PropertyValueScope[] | undefined,
}));

vi.mock("@/sdk/BApi", () => ({
  default: {
    resourceProfile: { addResourceProfile: mocks.create },
    resource: { searchResources: mocks.search },
  },
}));
vi.mock("@/components/bakaui", async () => ({
  ...(await vi.importActual("@heroui/react")),
  Modal: (props: ModalProps) => <SharedModal disableAnimation {...props} />,
  toast: { danger: mocks.toast },
}));
vi.mock("@/components/Resource/components/DetailModal", () => ({ default: () => null }));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.portal }),
}));
vi.mock("@/stores/options", () => ({
  useResourceOptionsStore: (
    selector: (state: { data: { propertyValueScopePriority?: PropertyValueScope[] } }) => unknown,
  ) => selector({ data: { propertyValueScopePriority: mocks.scopePriority } }),
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values?: object) => (values ? `${key}:${JSON.stringify(values)}` : key),
  }),
}));

let host: HTMLDivElement;
let root: Root;

function profile(overrides: Partial<Profile> = {}): Profile {
  return {
    id: 5,
    name: "Reading",
    priority: 10,
    createdAt: "2026-09-01T00:00:00Z",
    updatedAt: "2026-09-01T00:00:00Z",
    ...overrides,
  };
}
function deferred() {
  let resolve!: (value?: unknown) => void;
  let reject!: (error: Error) => void;
  const promise = new Promise((res, rej) => {
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
function button(text: string) {
  return [...document.querySelectorAll("button, [role=button]")].find(
    (element) => element.textContent === text,
  )!;
}
function input(label: string) {
  return document.querySelector<HTMLInputElement>(`[aria-label="${label}"]`)!;
}
async function type(label: string, value: string) {
  await act(async () => {
    const element = input(label);

    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
  });
}
function resources(ids: number[], totalCount = ids.length) {
  return {
    code: 0,
    totalCount,
    data: ids.map((id) => ({ id, displayName: `Resource ${id}`, path: `/library/${id}.zip` })),
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mocks.create.mockReset();
  mocks.search.mockReset();
  mocks.scopePriority = undefined;
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});
afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("resource profile basic information", () => {
  it("creates a trimmed profile and passes the actual returned VM to onSaved after success", async () => {
    const pending = deferred();

    mocks.create.mockReturnValueOnce(pending.promise);
    const onSaved = vi.fn();

    await render(
      <ResourceProfileModal
        existingNames={["resourceProfile.label.resourceProfile 1"]}
        onSaved={onSaved}
      />,
    );
    expect(input("resourceProfile.label.name")).toHaveValue(
      "resourceProfile.label.resourceProfile 2",
    );
    expect(document.body).toHaveTextContent("resourceProfile.basic.newScopeHint");
    await type("resourceProfile.label.name", "  My collection  ");
    await type("resourceProfile.label.priority", "-3");
    await click(button("resourceProfile.basic.createAndConfigure"));
    expect(mocks.create).toHaveBeenCalledWith({ name: "My collection", priority: -3 });
    expect(onSaved).not.toHaveBeenCalled();
    expect(button("common.action.cancel")).toBeDisabled();
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    const created = profile({ id: 11, name: "My collection", priority: -3 });

    await act(async () => pending.resolve({ code: 0, data: created }));
    expect(onSaved).toHaveBeenCalledExactlyOnceWith(created);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("edits through onUpdate only and preserves edited values if saving fails", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const pending = deferred();
    const onUpdate = vi.fn().mockReturnValueOnce(pending.promise).mockResolvedValueOnce(undefined);
    const onSaved = vi.fn();

    await render(
      <ResourceProfileModal profile={profile()} onSaved={onSaved} onUpdate={onUpdate} />,
    );
    await type("resourceProfile.label.name", "  Renamed  ");
    await type("resourceProfile.label.priority", "21");
    await click(button("common.action.save"));
    expect(onUpdate).toHaveBeenCalledWith(5, { name: "Renamed", priority: 21 });
    expect(mocks.create).not.toHaveBeenCalled();
    await act(async () => pending.reject(new Error("Server unavailable")));
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    expect(document.querySelector('[role="alert"]')).toHaveTextContent("Server unavailable");
    expect(input("resourceProfile.label.name")).toHaveValue("  Renamed  ");
    expect(input("resourceProfile.label.priority")).toHaveValue(21);
    await click(button("common.action.save"));
    expect(onSaved).not.toHaveBeenCalled();
    expect(onUpdate).toHaveBeenCalledTimes(2);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it("never falls back to creating a profile when an editor callback is missing", async () => {
    await render(<ResourceProfileModal profile={profile()} />);
    expect(button("common.action.save")).toBeDisabled();
    expect(document.querySelector('[role="alert"]')).toHaveTextContent(
      "resourceProfile.basic.editUnavailable",
    );
    await click(button("common.action.save"));
    expect(mocks.create).not.toHaveBeenCalled();
  });

  it("validates names and integer priorities and keeps a failed create open for retry", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    const onSaved = vi.fn();

    await render(<ResourceProfileModal onSaved={onSaved} />);
    await type("resourceProfile.label.name", "   ");
    expect(button("resourceProfile.basic.createAndConfigure")).toBeDisabled();
    await type("resourceProfile.label.name", "Valid name");
    await type("resourceProfile.label.priority", "1.5");
    expect(button("resourceProfile.basic.createAndConfigure")).toBeDisabled();
    await type("resourceProfile.label.priority", "2147483648");
    expect(button("resourceProfile.basic.createAndConfigure")).toBeDisabled();
    await type("resourceProfile.label.priority", "3");
    mocks.create.mockResolvedValueOnce({ code: 400, data: profile(), message: "Rejected" });
    await click(button("resourceProfile.basic.createAndConfigure"));
    expect(document.querySelector('[role="alert"]')).toHaveTextContent("Rejected");
    expect(input("resourceProfile.label.name")).toHaveValue("Valid name");
    expect(onSaved).not.toHaveBeenCalled();
    mocks.create.mockResolvedValueOnce({ code: 0 });
    await click(button("resourceProfile.basic.createAndConfigure"));
    expect(onSaved).not.toHaveBeenCalled();
    expect(document.querySelector('[role="dialog"]')).not.toBeNull();
    mocks.create.mockResolvedValueOnce({ code: 0, data: profile({ name: "Valid name" }) });
    await click(button("resourceProfile.basic.createAndConfigure"));
    expect(onSaved).toHaveBeenCalledTimes(1);
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });
});

describe("resource profile match preview", () => {
  it("treats legacy keyword-only profiles as matching all resources", () => {
    expect(hasProfileConditions({ page: 1, pageSize: 25, keyword: "legacy" })).toBe(false);
    expect(hasProfileConditions({ page: 1, pageSize: 25, tags: [ResourceTag.Pinned] })).toBe(true);
    expect(
      hasProfileConditions({
        page: 1,
        pageSize: 25,
        group: {
          combinator: SearchCombinator.And,
          disabled: true,
          filters: [{ operation: SearchOperation.IsNotNull, disabled: false }],
        },
      }),
    ).toBe(false);
  });

  const filtered = profile({
    search: {
      page: 9,
      pageSize: 200,
      keyword: "author",
      orders: [{ property: 6, asc: false }],
      tags: [ResourceTag.Pinned],
      group: {
        combinator: SearchCombinator.And,
        disabled: false,
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.MediaLibraryV2Multi,
            operation: SearchOperation.In,
            dbValue: "7",
            bizValue: "Library Seven",
            disabled: false,
          },
        ],
      },
    },
  });

  it("previews effective groups and tags while ignoring legacy keywords and loading only display names", async () => {
    mocks.search.mockResolvedValueOnce({
      code: 0,
      totalCount: 2,
      data: [
        { id: 1, displayName: "Actual display name", path: "/library/actual.zip" },
        { id: 2, displayName: "Remote resource" },
      ],
    });
    await render(<ResourceProfileTestModal isDraft profile={filtered} />);
    expect(mocks.search.mock.calls[0][0]).toEqual({
      page: 1,
      pageSize: 25,
      tags: [ResourceTag.Pinned],
      group: {
        combinator: SearchCombinator.And,
        disabled: false,
        groups: undefined,
        filters: [
          {
            propertyPool: PropertyPool.Internal,
            propertyId: InternalProperty.MediaLibraryV2Multi,
            operation: SearchOperation.In,
            dbValue: "7",
            disabled: false,
          },
        ],
      },
    });
    expect(mocks.search.mock.calls[0][1]).toEqual({
      additionalItems: ResourceAdditionalItem.DisplayName,
      saveSearch: false,
    });
    expect(mocks.search.mock.calls[0][0]).not.toHaveProperty("additionalItems");
    expect(mocks.search.mock.calls[0][0]).not.toHaveProperty("keyword");
    expect(mocks.search.mock.calls[0][0]).not.toHaveProperty("orders");
    expect(document.body).toHaveTextContent("resourceProfile.preview.draftHint");
    expect(document.body).toHaveTextContent("Actual display name");
    expect(document.body).toHaveTextContent("/library/actual.zip");
    expect(document.body).toHaveTextContent("resourceProfile.preview.noLocalFile");
    expect(document.querySelector("input")).toBeNull();
    await click(
      [...document.querySelectorAll("button")].find((element) =>
        element.textContent?.includes("Actual display name"),
      )!,
    );
    expect(mocks.portal).toHaveBeenCalledWith(ResourceDetailModal, {
      id: 1,
      onDestroyed: expect.any(Function),
    });
  });

  it("shows a pathless resource's saved manual name and keeps display name and path fallbacks", async () => {
    const properties = {
      [PropertyPool.Reserved]: {
        [ReservedProperty.Name]: {
          type: PropertyType.SingleLineText,
          dbValueType: StandardValueType.String,
          bizValueType: StandardValueType.String,
          order: 0,
          values: [{ scope: PropertyValueScope.Manual, bizValue: "Saved manual name" }],
        },
      },
    };

    mocks.search.mockResolvedValueOnce({
      code: 0,
      totalCount: 4,
      data: [
        { id: 1, properties },
        { id: 2, displayName: "Resolved display name", properties },
        { id: 3, path: "C:\\library\\Local filename.zip" },
        { id: 4 },
      ],
    });
    await render(<ResourceProfileTestModal profile={profile()} />);
    expect(document.querySelector('[title="Saved manual name"]')).toHaveTextContent(
      "Saved manual name",
    );
    expect(document.querySelectorAll('[title="Saved manual name"]')).toHaveLength(1);
    expect(document.querySelector('[title="Resolved display name"]')).toBeInTheDocument();
    expect(document.querySelector('[title="Local filename.zip"]')).toBeInTheDocument();
    expect(document.body).toHaveTextContent('resourceProfile.preview.unnamed:{"id":4}');
    expect(mocks.search.mock.calls[0][1].additionalItems).toBe(ResourceAdditionalItem.DisplayName);
  });

  it("resolves name scope priority using resource overrides, then profiles, then global preferences", async () => {
    mocks.scopePriority = [PropertyValueScope.Synchronization, PropertyValueScope.Manual];
    const named = (id: number, profileScopePriority?: PropertyValueScope[]) => ({
      id,
      properties: {
        [PropertyPool.Reserved]: {
          [ReservedProperty.Name]: {
            type: PropertyType.SingleLineText,
            dbValueType: StandardValueType.String,
            bizValueType: StandardValueType.String,
            order: 0,
            profileScopePriority,
            values: [
              {
                scope: PropertyValueScope.Manual,
                bizValue: `Manual ${id}`,
                aliasAppliedBizValue: `Manual alias ${id}`,
              },
              { scope: PropertyValueScope.Synchronization, bizValue: `Synchronized ${id}` },
            ],
          },
        },
      },
    });
    const preference = (resourceId: number, scope: PropertyValueScope) => [
      {
        resourceId,
        propertyPool: PropertyPool.Reserved,
        propertyId: ReservedProperty.Name,
        priorities: [{ scope, fallbackOnEmpty: false }],
      },
    ];

    mocks.search.mockResolvedValueOnce({
      code: 0,
      totalCount: 4,
      data: [
        named(1),
        named(2, [PropertyValueScope.Manual]),
        {
          ...named(3, [PropertyValueScope.Manual]),
          scopePreferences: preference(3, PropertyValueScope.Synchronization),
        },
        {
          ...named(4, [PropertyValueScope.Manual]),
          path: "/library/Name unavailable.zip",
          scopePreferences: preference(4, PropertyValueScope.Bangumi),
        },
      ],
    });
    await render(<ResourceProfileTestModal profile={profile()} />);
    expect(document.querySelector('[title="Synchronized 1"]')).toBeInTheDocument();
    expect(document.querySelector('[title="Manual alias 2"]')).toBeInTheDocument();
    expect(document.querySelector('[title="Synchronized 3"]')).toBeInTheDocument();
    expect(document.querySelector('[title="Name unavailable.zip"]')).toBeInTheDocument();
    expect(document.querySelector('[title="Manual alias 4"]')).not.toBeInTheDocument();
  });

  it("loads page two from the server and resets to page one when the criteria change", async () => {
    mocks.search
      .mockResolvedValueOnce(resources([1], 26))
      .mockResolvedValueOnce(resources([26], 26))
      .mockResolvedValueOnce(resources([99], 1));
    await render(<ResourceProfileTestModal profile={filtered} />);
    await click(button("2"));
    expect(mocks.search.mock.calls[1][0]).toMatchObject({
      page: 2,
      pageSize: 25,
      tags: [ResourceTag.Pinned],
    });
    expect(document.body).toHaveTextContent("Resource 26");
    expect(document.body).not.toHaveTextContent("Resource 1");
    await render(
      <ResourceProfileTestModal
        profile={profile({ search: { page: 7, pageSize: 100, keyword: "new" } })}
      />,
    );
    expect(mocks.search.mock.calls[2][0]).toMatchObject({ page: 1, pageSize: 25 });
    expect(document.body).toHaveTextContent("Resource 99");
  });

  it("ignores outdated or unmounted requests instead of overwriting a newer preview", async () => {
    const previous = deferred();
    const latest = deferred();
    const unmounted = deferred();

    mocks.search
      .mockReturnValueOnce(previous.promise)
      .mockReturnValueOnce(latest.promise)
      .mockReturnValueOnce(unmounted.promise);
    await render(<ResourceProfileTestModal profile={filtered} />);
    const oldSignal = mocks.search.mock.calls[0][2].signal;

    await render(
      <ResourceProfileTestModal
        profile={profile({
          search: { page: 1, pageSize: 25, keyword: "other", tags: [ResourceTag.Pinned] },
        })}
      />,
    );
    expect(oldSignal.aborted).toBe(true);
    await act(async () => latest.resolve(resources([2])));
    await act(async () => previous.resolve(resources([1])));
    expect(document.body).toHaveTextContent("Resource 2");
    expect(document.body).not.toHaveTextContent("Resource 1");
    await render(<ResourceProfileTestModal profile={profile()} />);
    const lastSignal = mocks.search.mock.calls[2][2].signal;

    await render(<></>);
    expect(lastSignal.aborted).toBe(true);
    await act(async () => unmounted.resolve(resources([3])));
    expect(document.querySelector('[role="dialog"]')).toBeNull();
  });

  it.each(["api", "network"])(
    "shows a retryable %s failure rather than an empty or successful preview",
    async (kind) => {
      if (kind === "api")
        mocks.search.mockResolvedValueOnce({ code: 500, data: [], totalCount: 0 });
      else mocks.search.mockRejectedValueOnce(new Error("offline"));
      await render(<ResourceProfileTestModal profile={profile()} />);
      expect(document.querySelector('[role="alert"]')).toHaveTextContent(
        "resourceProfile.preview.loadFailed",
      );
      expect(document.body).not.toHaveTextContent(
        "resourceProfile.empty.noResourcesMatchedByCriteria",
      );
      expect(document.querySelector("strong")).toHaveTextContent("—");
      mocks.search.mockResolvedValueOnce(resources([]));
      await click(button("resourceProfile.preview.retry"));
      expect(document.body).toHaveTextContent("resourceProfile.empty.noResourcesMatchedByCriteria");
      expect(document.querySelector("strong")).toHaveTextContent("0");
      expect(document.querySelector('[role="alert"]')).toBeNull();
      expect(document.querySelector('[role="status"]')).toBeNull();
    },
  );
});
