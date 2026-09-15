import type * as FirstRunHelp from "@/components/HelpCenter/useFirstRunHelp";
import type { HelpCenterModalProps } from "@/components/HelpCenter/HelpCenterModal";

import { act } from "react-dom/test-utils";
import { createRoot, type Root } from "react-dom/client";
import { HeroUIProvider } from "@heroui/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DashboardPage from "../index";
import { useDashboardOverview } from "../hooks/useDashboardOverview";

import { GETTING_STARTED_FIRST_RUN_KEY } from "@/components/HelpCenter/useFirstRunHelp";
import { deserializeStandardValue } from "@/components/StandardValue/helpers";
import {
  InternalProperty,
  PropertyPool,
  SearchCombinator,
  SearchOperation,
  StandardValueType,
} from "@/sdk/constants";

const mocks = vi.hoisted(() => ({ overview: vi.fn(), navigate: vi.fn(), pendingSearch: vi.fn() }));

vi.mock("@/sdk/BApi", () => ({ default: { dashboard: { getDashboardOverview: mocks.overview } } }));
vi.mock("react-router-dom", () => ({ useNavigate: () => mocks.navigate }));
vi.mock("@/components/bakaui", async () => await vi.importActual("@heroui/react"));
vi.mock("@/stores/pendingSearch", () => ({
  usePendingSearchStore: (selector: (store: unknown) => unknown) =>
    selector({ setPendingSearch: mocks.pendingSearch }),
}));
vi.mock("../components/RecentResources", () => ({
  default: ({ refreshKey }: { refreshKey: number }) => (
    <div data-recent-refresh={refreshKey}>Recent resources</div>
  ),
}));
vi.mock("../components/ActivityOverview", () => ({
  default: () => <div data-activity>Activity</div>,
}));
vi.mock("../components/DataMigrationHintModal", () => ({
  DataMigrationHintModal: () => <div data-migration-hint />,
}));
vi.mock("@/components/HelpCenter", async () => ({
  ...(await vi.importActual<typeof FirstRunHelp>("@/components/HelpCenter/useFirstRunHelp")),
  HelpCenterModal: ({ visible, firstRun, topic, onClose }: HelpCenterModalProps) =>
    visible ? (
      <div data-help-first-run={String(firstRun)} data-help-topic={topic} role="dialog">
        <button onClick={onClose}>Complete guide</button>
      </div>
    ) : null,
}));

let host: HTMLDivElement;
let root: Root;

function overview(totalResourceCount = 30) {
  return {
    code: 0,
    data: {
      totalResourceCount,
      localResourceCount: totalResourceCount === 0 ? 0 : 20,
      pendingResourceCount: totalResourceCount === 0 ? 0 : 10,
      collectionCount: totalResourceCount === 0 ? 0 : 4,
      mediaLibraryCount: 1,
      thisWeekAddedCount: 3,
      mediaLibraries: [{ id: 7, name: "Library Seven", resourceCount: 12 }],
      workflows: { runningCount: 1, waitingCount: 2, failedRecentlyCount: 0, attentionRuns: [] },
    },
  };
}

function deferred() {
  let resolve!: (value: unknown) => void;
  let reject!: (reason: Error) => void;
  const promise = new Promise((res, rej) => {
    resolve = res;
    reject = rej;
  });

  return { promise, resolve, reject };
}

async function renderPage() {
  await act(async () =>
    root.render(
      <HeroUIProvider disableAnimation>
        <DashboardPage />
      </HeroUIProvider>,
    ),
  );
}

async function click(element: Element) {
  await act(async () => (element as HTMLElement).click());
}

function button(text: string) {
  return [...host.querySelectorAll("button")].find((element) => element.textContent === text)!;
}

function card(label: string) {
  return [...host.querySelectorAll("button")].find((element) =>
    element.textContent?.includes(label),
  )!;
}

function metric(label: string) {
  return card(label).querySelector(".text-3xl")!.textContent;
}

function refreshButton() {
  return host.querySelector<HTMLButtonElement>('[aria-label="dashboard.action.refresh"]')!;
}

async function typeKeyword(value: string) {
  await act(async () => {
    const input = host.querySelector<HTMLInputElement>('[aria-label="dashboard.search.label"]')!;

    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(input, value);
    input.dispatchEvent(new Event("input", { bubbles: true }));
  });
}

async function submitSearch() {
  await act(async () => {
    host
      .querySelector("form")!
      .dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("IS_REACT_ACT_ENVIRONMENT", true);
  localStorage.clear();
  localStorage.setItem(GETTING_STARTED_FIRST_RUN_KEY, "true");
  host = document.createElement("div");
  document.body.appendChild(host);
  root = createRoot(host);
});

afterEach(async () => {
  await act(async () => root.unmount());
  host.remove();
  localStorage.clear();
  vi.unstubAllGlobals();
});

describe("dashboard overview and entry points", () => {
  it("keeps unknown metrics distinct from zero and recovers from a failed initial overview", async () => {
    const first = deferred();

    mocks.overview.mockReturnValueOnce(first.promise);
    await renderPage();
    const labels = ["totalResources", "localResources", "pendingResources", "collections"];

    for (const label of labels) expect(metric(`dashboard.stat.${label}`)).toBe("—");
    expect(host.querySelector('[aria-label="dashboard.overview.title"]')).toHaveAttribute(
      "aria-busy",
      "true",
    );
    expect(host.querySelector("[data-recent-refresh]")).toHaveAttribute("data-recent-refresh", "0");

    await act(async () => first.resolve({ code: 500, message: "unavailable" }));
    expect(host.querySelector('[role="alert"]')).toHaveTextContent("dashboard.error.load");
    expect(host.querySelector('[aria-label="dashboard.overview.title"]')).toHaveAttribute(
      "aria-busy",
      "false",
    );
    for (const label of labels) expect(metric(`dashboard.stat.${label}`)).toBe("—");
    expect(refreshButton()).not.toBeDisabled();
    expect(host.querySelector("[data-activity]")).toBeNull();

    mocks.overview.mockResolvedValueOnce(overview(0));
    await click(button("dashboard.action.retry"));
    for (const label of labels) expect(metric(`dashboard.stat.${label}`)).toBe("0");
    expect(host.querySelector('[role="alert"]')).toBeNull();
    expect(host.querySelector("[data-recent-refresh]")).toHaveAttribute("data-recent-refresh", "1");
    expect(host.querySelector("[data-activity]")).not.toBeNull();
  });

  it("retains valid metrics after a refresh fails and refreshes recent resources on each retry", async () => {
    mocks.overview.mockResolvedValueOnce(overview(30));
    await renderPage();
    mocks.overview.mockRejectedValueOnce(new Error("offline"));
    await click(refreshButton());
    expect(metric("dashboard.stat.totalResources")).toBe("30");
    expect(host.querySelector('[role="alert"]')).toHaveTextContent("dashboard.error.stale");
    expect(host.querySelector("[data-recent-refresh]")).toHaveAttribute("data-recent-refresh", "1");
    expect(refreshButton()).not.toBeDisabled();
    expect(mocks.pendingSearch).not.toHaveBeenCalled();
    expect(mocks.navigate).not.toHaveBeenCalled();

    mocks.overview.mockResolvedValueOnce(overview(31));
    await click(button("dashboard.action.retry"));
    expect(metric("dashboard.stat.totalResources")).toBe("31");
    expect(host.querySelector("[data-recent-refresh]")).toHaveAttribute("data-recent-refresh", "2");
    expect(mocks.overview).toHaveBeenCalledTimes(3);
  });

  it("opens resource searches with a trimmed keyword and correctly serialized local/library filters", async () => {
    mocks.overview.mockResolvedValueOnce(overview());
    await renderPage();
    await typeKeyword("  summer archive  ");
    await submitSearch();
    expect(mocks.pendingSearch).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 100,
      keyword: "summer archive",
    });
    expect(mocks.navigate).toHaveBeenLastCalledWith("/resource");
    await typeKeyword("   ");
    await submitSearch();
    expect(mocks.pendingSearch).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 100,
      keyword: undefined,
    });

    await click(card("dashboard.stat.totalResources"));
    expect(mocks.pendingSearch).toHaveBeenLastCalledWith({
      page: 1,
      pageSize: 100,
      keyword: undefined,
    });
    await click(card("dashboard.stat.localResources"));
    const local = mocks.pendingSearch.mock.lastCall![0];

    expect(local.group).toEqual({
      combinator: SearchCombinator.And,
      disabled: false,
      filters: [
        {
          propertyPool: PropertyPool.Internal,
          propertyId: InternalProperty.HasLocalPath,
          operation: SearchOperation.Equals,
          dbValue: "True",
          disabled: false,
        },
      ],
    });
    expect(
      deserializeStandardValue(local.group.filters[0].dbValue, StandardValueType.Boolean),
    ).toBe(true);

    await click(card("Library Seven"));
    const library = mocks.pendingSearch.mock.lastCall![0];

    expect(library.group).toEqual({
      combinator: SearchCombinator.And,
      disabled: false,
      filters: [
        {
          propertyPool: PropertyPool.Internal,
          propertyId: InternalProperty.MediaLibraryV2Multi,
          operation: SearchOperation.In,
          dbValue: "7",
          disabled: false,
        },
      ],
    });
    expect(
      deserializeStandardValue(library.group.filters[0].dbValue, StandardValueType.ListString),
    ).toEqual(["7"]);
    expect(mocks.navigate).toHaveBeenLastCalledWith("/resource");
  });

  it("keeps summary, tool and library-management links separate from resource searches", async () => {
    mocks.overview.mockResolvedValueOnce(overview());
    await renderPage();
    for (const [label, path] of [
      ["dashboard.stat.pendingResources", "/acquisitions"],
      ["dashboard.stat.collections", "/collections"],
      ["dashboard.shortcuts.local.title", "/path-mark-config"],
      ["dashboard.shortcuts.organize.title", "/file-processor"],
      ["dashboard.shortcuts.parse.title", "/post-parser"],
      ["dashboard.libraries.manage", "/media-library"],
    ]) {
      await click(card(label));
      expect(mocks.navigate).toHaveBeenLastCalledWith(path);
    }
    expect(mocks.pendingSearch).not.toHaveBeenCalled();
  });

  it("preserves the first-run getting-started guide, completion key and migration hint", async () => {
    localStorage.removeItem(GETTING_STARTED_FIRST_RUN_KEY);
    mocks.overview.mockResolvedValue(overview());
    await renderPage();
    expect(host.querySelector('[role="dialog"]')).toHaveAttribute(
      "data-help-topic",
      "gettingStarted",
    );
    expect(host.querySelector('[role="dialog"]')).toHaveAttribute("data-help-first-run", "true");
    expect(host.querySelector("[data-migration-hint]")).not.toBeNull();
    await click(button("Complete guide"));
    expect(host.querySelector('[role="dialog"]')).toBeNull();
    expect(localStorage.getItem(GETTING_STARTED_FIRST_RUN_KEY)).toBe("true");
    await act(async () => root.render(<></>));
    await renderPage();
    expect(host.querySelector('[role="dialog"]')).toBeNull();
  });

  it("ignores older success/failure responses and unfinished work after unmount", async () => {
    const first = deferred();
    const second = deferred();
    const latest = deferred();
    const unmounted = deferred();

    mocks.overview
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)
      .mockReturnValueOnce(latest.promise)
      .mockReturnValueOnce(unmounted.promise);
    let observed!: ReturnType<typeof useDashboardOverview>;
    let renderCount = 0;

    function Harness({ refreshKey }: { refreshKey: number }) {
      observed = useDashboardOverview(refreshKey);
      renderCount++;

      return null;
    }
    for (const refreshKey of [0, 1, 2]) {
      await act(async () => root.render(<Harness refreshKey={refreshKey} />));
    }
    await act(async () => latest.resolve(overview(50)));
    const updatedAt = observed.updatedAt;

    expect(observed.data?.totalResourceCount).toBe(50);
    await act(async () => {
      first.resolve(overview(10));
      second.reject(new Error("old request failed"));
    });
    expect(observed.data?.totalResourceCount).toBe(50);
    expect(observed.updatedAt).toBe(updatedAt);
    expect(observed.loading).toBe(false);
    expect(observed.error).toBe(false);

    await act(async () => root.render(<Harness refreshKey={3} />));
    await act(async () => root.render(<></>));
    const before = renderCount;

    await act(async () => unmounted.resolve(overview(99)));
    expect(renderCount).toBe(before);
    expect(observed.data?.totalResourceCount).toBe(50);
  });

  it("treats a missing overview payload as a recoverable error instead of a blank or zero dashboard", async () => {
    mocks.overview.mockResolvedValueOnce({ code: 0 });
    await renderPage();
    expect(host.querySelector('[role="alert"]')).toHaveTextContent("dashboard.error.load");
    expect(metric("dashboard.stat.totalResources")).toBe("—");
    expect(host.querySelector('[aria-label="dashboard.overview.title"]')).toHaveAttribute(
      "aria-busy",
      "false",
    );
    expect(refreshButton()).not.toBeDisabled();
    expect(host.querySelector("[data-recent-refresh]")).not.toBeNull();
  });
});
