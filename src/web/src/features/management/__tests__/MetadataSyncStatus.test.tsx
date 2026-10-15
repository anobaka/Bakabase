import type { ManagementSource } from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { MetadataSyncStatus } from "../MetadataSyncStatus";

const { pause } = vi.hoisted(() => ({ pause: vi.fn() }));

vi.mock("../DeviceDot", () => ({
  default: ({ name }: { name: string }) => <i aria-label={name} />,
}));
vi.mock("../api", () => ({ setMetadataSyncPaused: pause }));
vi.mock("../useManagementSources", () => ({
  useManagementSources: () => ({ sources: [], refresh: vi.fn() }),
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, data?: { count?: number }) =>
      `${key}${data?.count != null ? ` ${data.count}` : ""}`,
    i18n: { language: "en" },
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled, isLoading, ...rest }: any) => (
    <button {...rest} disabled={isDisabled || isLoading} onClick={onPress}>
      {children}
    </button>
  ),
}));
const source = (): ManagementSource => ({
  serverId: "remote",
  libraryEpoch: "library",
  name: "NAS",
  available: true,
  isLocal: false,
  contractVersion: 1,
  metadata: {
    identity: { serverId: "remote", libraryEpoch: "library" },
    name: "NAS",
    denied: false,
    data: { resourceCount: 0, activeSequence: 0, stagedResourceCount: 1200 },
    sync: { paused: false, isOffline: false },
  },
});

beforeEach(() => {
  pause.mockReset().mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("resource list sync status", () => {
  it("asks to update an older source instead of presenting it as an offline empty library", () => {
    const owner = source();

    owner.libraryEpoch = undefined;
    owner.available = false;
    owner.metadata!.sync.isOffline = true;
    owner.metadata!.sync.errorCode = "ManagementUpdateRequired";
    render(<MetadataSyncStatus sources={[owner]} onRefresh={vi.fn()} />);
    expect(screen.getByText("management.metadata.updateRequired")).toBeTruthy();
    expect(screen.getByText("management.metadata.updateRequiredHelp")).toBeTruthy();
    expect(screen.queryByText("management.metadata.offline")).toBeNull();
    expect(screen.getByRole("button", { name: "management.metadata.pause" })).toBeDisabled();
  });
  it("keeps the completed library visible while refreshing its catalog", () => {
    const owner = source();

    owner.metadata!.data.activeGeneration = "complete";
    owner.metadata!.data.resourceCount = 100000;
    owner.metadata!.data.checkpoint = {
      generation: "complete",
      sequence: 42,
      cursor: "catalog-refresh",
      complete: true,
    };
    render(<MetadataSyncStatus sources={[owner]} onRefresh={vi.fn()} />);
    expect(screen.getByText("management.metadata.updating")).toBeTruthy();
    expect(screen.getByText("management.metadata.resourceCount 100000")).toBeTruthy();
    expect(screen.queryByText("management.metadata.ready")).toBeNull();
  });
  it("keeps completed resource counts while explicitly waiting for a saved change to refresh", () => {
    const owner = source();

    owner.metadata!.data.activeGeneration = "complete";
    owner.metadata!.data.resourceCount = 23;
    owner.metadata!.sync.refreshPending = true;
    render(<MetadataSyncStatus sources={[owner]} onRefresh={vi.fn()} />);
    expect(screen.getByText("management.metadata.updating")).toBeTruthy();
    expect(screen.getByText("management.metadata.refreshPendingHelp")).toBeTruthy();
    expect(screen.getByText("management.metadata.resourceCount 23")).toBeTruthy();
    expect(screen.queryByText("management.metadata.incompleteHelp")).toBeNull();
  });
  it("shows saved first-sync progress without presenting an empty library as complete", () => {
    render(<MetadataSyncStatus sources={[source()]} onRefresh={vi.fn()} />);
    expect(screen.getByText("management.metadata.pendingSummary 1")).toBeTruthy();
    expect(screen.getByText("management.metadata.stagedCount 1200")).toBeTruthy();
    expect(screen.getByText("management.metadata.incompleteHelp")).toBeTruthy();
    expect(screen.queryByText("management.metadata.ready")).toBeNull();
  });
  it("pauses and resumes only the explicitly chosen owner, retaining UI errors on failed controls", async () => {
    const owner = source();
    const refresh = vi.fn();
    const view = render(<MetadataSyncStatus sources={[owner]} onRefresh={refresh} />);

    fireEvent.click(screen.getByText("management.metadata.pause"));
    await waitFor(() => expect(pause).toHaveBeenCalledWith(owner, true));
    expect(refresh).toHaveBeenCalledOnce();
    const paused = {
      ...owner,
      metadata: { ...owner.metadata!, sync: { ...owner.metadata!.sync, paused: true } },
    };

    view.rerender(<MetadataSyncStatus sources={[paused]} onRefresh={refresh} />);
    pause.mockRejectedValue(new Error("Cannot save settings"));
    await act(async () => fireEvent.click(screen.getByText("management.metadata.resume")));
    expect(pause).toHaveBeenLastCalledWith(paused, false);
    expect(screen.getByRole("alert")).toHaveTextContent("Cannot save settings");
  });
  it("explains cached offline results and hides itself for an ordinary local library", () => {
    const owner = source();

    owner.available = false;
    owner.metadata!.data.activeGeneration = "complete";
    owner.metadata!.data.resourceCount = 23;
    owner.metadata!.sync.isOffline = true;
    const view = render(<MetadataSyncStatus sources={[owner]} onRefresh={vi.fn()} />);

    expect(screen.getByText("management.metadata.cachedHelp")).toBeTruthy();
    expect(screen.getByText("management.metadata.resourceCount 23")).toBeTruthy();
    view.rerender(
      <MetadataSyncStatus sources={[{ ...owner, isLocal: true }]} onRefresh={vi.fn()} />,
    );
    expect(view.container).toBeEmptyDOMElement();
  });
});
