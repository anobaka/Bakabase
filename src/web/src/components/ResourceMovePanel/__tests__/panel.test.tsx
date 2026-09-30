import type { MoveBatch } from "../types";

import React from "react";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  context: vi.fn(),
  options: vi.fn(),
  saveOptions: vi.fn(),
  batches: vi.fn(),
  activeBatches: vi.fn(),
  preview: vi.fn(),
  create: vi.fn(),
  cancel: vi.fn(),
  retry: vi.fn(),
  resolve: vi.fn(),
}));

vi.mock("../api", () => ({ movePanelApi: api, MoveRequestRejected: class extends Error {} }));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "/api" } }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    pathMark: {
      getAllPathMarks: vi.fn().mockResolvedValue({ data: [] }),
      getAllPathMarkPaths: vi.fn().mockResolvedValue({ data: [] }),
    },
    file: { getIwFsEntry: vi.fn().mockResolvedValue({ data: { path: "/target", type: 100 } }) },
    tool: { openFileOrDirectory: vi.fn().mockResolvedValue({}) },
  },
}));
vi.mock("@/components/FileExplorer", () => ({ FileExplorer: () => <div>Folder picker</div> }));
vi.mock("@/components/bakaui", async () => {
  const { Button, Checkbox, Chip, Input, Progress, Select, SelectItem } = await import(
    "@heroui/react"
  );

  return {
    Button,
    Checkbox,
    Chip,
    Input,
    Progress,
    Select: ({ dataSource, ...props }: any) => (
      <Select {...props}>
        {(dataSource ?? []).map((item: any) => (
          <SelectItem key={item.value}>{item.label}</SelectItem>
        ))}
      </Select>
    ),
    Modal: ({ children, title, visible, onClose, hideCloseButton }: any) =>
      visible ? (
        <div aria-label={title} role="dialog">
          {!hideCloseButton && <button onClick={onClose}>Close dialog</button>}
          {children}
        </div>
      ) : null,
    Spinner: () => <span>Loading</span>,
  };
});
vi.mock("react-rnd", () => ({ Rnd: ({ children }: any) => <div>{children}</div> }));
import ResourceMovePanel from "../index";
import MoveConfirmation from "../MoveConfirmation";
import { MoveTaskCard } from "../TaskList";

import BApi from "@/sdk/BApi";
import { prepareMove, useResourceMovePanelStore } from "@/stores/resourceMovePanel";

const initial = useResourceMovePanelStore.getState();
const sourceContext = { nodeId: "this-server", libraryEpoch: "this-library" };
const destination = { id: "dest", path: "/target", scope: "global" as const, order: 0 };
const options = { revision: 1, destinations: [destination], autoOverwrite: false };
const preview = {
  previewFingerprint: "verified",
  items: [
    {
      resourceId: 1,
      sourcePath: "/source/a",
      destPath: "/target/a",
      effects: [],
      coveredResources: [],
    },
  ],
};

beforeEach(() => {
  vi.clearAllMocks();
  Object.values(api).forEach((mock) => mock.mockReset());
  api.context.mockResolvedValue(sourceContext);
  vi.mocked(BApi.pathMark.getAllPathMarks).mockResolvedValue({ data: [] } as any);
  vi.mocked(BApi.pathMark.getAllPathMarkPaths).mockResolvedValue({ data: [] } as any);
  vi.mocked(BApi.file.getIwFsEntry).mockResolvedValue({
    data: { path: "/target", type: 100 },
  } as any);
  api.options.mockResolvedValue(options);
  api.batches.mockResolvedValue([]);
  api.activeBatches.mockResolvedValue([]);
  api.preview.mockResolvedValue(preview);
  api.saveOptions.mockResolvedValue({ ...options, revision: 2 });
  useResourceMovePanelStore.setState(
    {
      ...initial,
      sourceContext,
      open: true,
      panelEnabled: true,
      initialized: true,
      options,
      context: {
        tabId: "a",
        tabName: "Search A",
        resources: [{ id: 1, path: "/source/a" }],
        selectedResources: [{ id: 1, path: "/source/a" }],
      },
    },
    true,
  );
});
afterEach(cleanup);
describe("real move panel interactions", () => {
  it("never submits until the mandatory preview confirmation is accepted", async () => {
    api.create.mockResolvedValue({ batchId: "b", skippedResourceCount: 0 });
    render(<ResourceMovePanel />);
    fireEvent.click(await screen.findByRole("button", { name: "Move selected (1)" }));
    const confirm = await screen.findByRole("dialog", { name: "Confirm resource move" });

    await waitFor(() =>
      expect(within(confirm).getByRole("button", { name: "Confirm move 1" })).toBeEnabled(),
    );
    expect(api.create).not.toHaveBeenCalled();
    expect(within(confirm).getAllByText("/target").length).toBeGreaterThan(0);
    fireEvent.click(within(confirm).getByRole("button", { name: "Confirm move 1" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.calls[0][0]).toMatchObject({
      resourceIds: [1],
      destDir: "/target",
      origin: "move-panel",
      sourceTabId: "a",
      resourceRefs: [{ ...sourceContext, resourceId: 1 }],
      expectedPreviewFingerprint: "verified",
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
  it("keeps an old confirmation disabled when the library at the same origin has changed", async () => {
    await prepareMove(destination, { resources: [{ id: 1, path: "/source/a" }] });
    render(<MoveConfirmation />);
    api.context.mockResolvedValue({ ...sourceContext, libraryEpoch: "new-library" });
    fireEvent.click(screen.getByRole("button", { name: "Confirm move 1" }));

    expect(
      await screen.findByText(
        "The library at this address changed. Reload before starting another move. Return to the original library to check an unconfirmed submission.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Confirm move 1" })).toBeDisabled();
    expect(api.create).not.toHaveBeenCalled();
  });
  it("confirms only the effective move set while retaining its server preview fingerprint", async () => {
    useResourceMovePanelStore.setState({
      context: {
        tabId: "a",
        resources: [{ id: 1, path: "/source/a" }, { id: 2 }],
        selectedResources: [{ id: 1, path: "/source/a" }, { id: 2 }],
      },
    });
    api.preview.mockResolvedValue({ ...preview, skippedResourceIds: [2] });
    api.create.mockResolvedValue({ batchId: "filtered", skippedResourceCount: 0 });
    render(<ResourceMovePanel />);
    fireEvent.click(await screen.findByRole("button", { name: "Move selected (2)" }));
    const confirm = await screen.findByRole("dialog");

    await waitFor(() =>
      expect(within(confirm).getByRole("button", { name: "Confirm move 1" })).toBeEnabled(),
    );
    expect(within(confirm).getByText("Excluded from this move: 1")).toBeInTheDocument();
    fireEvent.click(within(confirm).getByRole("button", { name: "Confirm move 1" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.calls[0][0]).toMatchObject({
      resourceIds: [1],
      expectedPreviewFingerprint: "verified",
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
  it("shows stopping progress instead of idle while minimized", async () => {
    const batch: MoveBatch = {
      batchId: "stopping",
      status: "stopping",
      percentage: 42,
      destDir: "/target",
      counts: { total: 1, succeeded: 0, failed: 0, cancelled: 0, skipped: 0, waiting: 0 },
      records: [],
    };

    useResourceMovePanelStore.setState({ open: false, minimized: true, batches: [batch] });
    api.batches.mockResolvedValue([batch]);
    render(<ResourceMovePanel />);
    expect(screen.getByRole("button", { name: "Expand move panel" })).toHaveTextContent(
      "Stopping 42%",
    );
    await waitFor(() => expect(api.batches).toHaveBeenCalled());
  });
  it("names authoritative Steam and remote exclusions while confirming the remaining local resource", async () => {
    const resources = [
      { id: 1, path: "/source/a", displayName: "Local book" },
      { id: 2, path: "/steam/game", displayName: "Old game name" },
      { id: 3, displayName: "Remote book" },
    ];

    useResourceMovePanelStore.setState({
      context: { tabId: "a", resources, selectedResources: resources },
    });
    api.preview.mockResolvedValue({
      ...preview,
      excludedResources: [
        {
          resourceId: 2,
          displayName: "Steam game",
          path: "/steam/game",
          reasonCode: "steamManaged",
        },
        { resourceId: 3, displayName: "Remote book", reasonCode: "noLocalFiles" },
      ],
    });
    api.create.mockResolvedValue({ batchId: "mixed", skippedResourceCount: 0 });
    render(<ResourceMovePanel />);
    fireEvent.click(await screen.findByRole("button", { name: "Move selected (3)" }));
    const confirm = await screen.findByRole("dialog");

    await waitFor(() =>
      expect(within(confirm).getByRole("button", { name: "Confirm move 1" })).toBeEnabled(),
    );
    expect(api.preview).toHaveBeenCalledWith(
      [1, 2, 3],
      "/target",
      [1, 2, 3].map((resourceId) => ({ ...sourceContext, resourceId })),
    );
    expect(within(confirm).getByText("Local book")).toBeVisible();
    expect(within(confirm).getByText("Steam game")).toBeVisible();
    expect(within(confirm).getByText("Remote book")).toBeVisible();
    expect(
      within(confirm).getByText(
        "The source or destination is managed by Steam. Move installations using Steam's storage settings.",
      ),
    ).toBeVisible();
    expect(
      within(confirm).getByText(
        "This resource has no local files to move. Download or install it first.",
      ),
    ).toBeVisible();
    expect(within(confirm).queryByText("steamManaged")).not.toBeInTheDocument();
    expect(within(confirm).getByText("Excluded from this move: 2")).toBeVisible();
    fireEvent.click(within(confirm).getByRole("button", { name: "Confirm move 1" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.calls[0][0]).toMatchObject({
      resourceIds: [1],
      expectedPreviewFingerprint: "verified",
    });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
  it("explains which Steam child prevents moving a whole root and disables confirmation", async () => {
    api.preview.mockResolvedValue({
      items: [
        {
          resourceId: 10,
          sourcePath: "/games",
          destPath: "/target/games",
          unavailableReason: "containsSteamManagedResource",
          coveredResources: [{ resourceId: 11, path: "/games/Steam game", wasSelected: false }],
        },
      ],
      excludedResources: [
        {
          resourceId: 10,
          displayName: "Game collection",
          path: "/games",
          reasonCode: "containsSteamManagedResource",
          blockingResourceIds: [11],
        },
      ],
    });
    await prepareMove(destination, { resources: [{ id: 10, path: "/games" }] });
    render(<MoveConfirmation />);

    expect(screen.getByText("Game collection")).toBeVisible();
    expect(
      screen.getByText(
        "This folder contains Steam-managed resources. The entire folder cannot be moved here; move those installations using Steam first.",
      ),
    ).toBeVisible();
    expect(screen.getByText("Blocking resources (1): Steam game")).toBeVisible();
    expect(
      screen.getByText("None of the selected resources can be moved. Review the reasons below."),
    ).toBeVisible();
    const confirm = screen.getByRole("button", { name: "Confirm move 0" });

    expect(confirm).toBeDisabled();
    fireEvent.click(confirm);
    expect(api.create).not.toHaveBeenCalled();
  });
  it("shows named exclusions even when there are no preview items", async () => {
    api.preview.mockResolvedValue({
      items: [],
      skippedResourceIds: [30],
      excludedResources: [
        { resourceId: 30, displayName: "Uninstalled game", reasonCode: "steamManaged" },
      ],
    });
    await prepareMove(destination, { resources: [{ id: 30 }] });
    render(<MoveConfirmation />);

    expect(screen.getByText("Uninstalled game")).toBeVisible();
    expect(
      screen.getByText(
        "The source or destination is managed by Steam. Move installations using Steam's storage settings.",
      ),
    ).toBeVisible();
    expect(screen.getByRole("button", { name: "Confirm move 0" })).toBeDisabled();
    expect(api.create).not.toHaveBeenCalled();
  });
  it("localizes source validation failures on tasks instead of showing the reason code", () => {
    render(
      <MoveTaskCard
        batch={{
          batchId: "source-changed",
          destDir: "/target",
          status: "failed",
          counts: { total: 1, succeeded: 0, failed: 1, cancelled: 0, skipped: 0, waiting: 0 },
          records: [
            {
              id: 1,
              resourceId: 1,
              sourcePath: "/source",
              destPath: "/target/source",
              status: 4,
              errorCode: "sourceLocationChanged",
              error: "sourceLocationChanged: source path no longer matches",
            },
          ],
        }}
      />,
    );

    expect(
      screen.getByText(
        "The resource path or source links changed after confirmation. Preview again if the move has not started; for a recovery task, restore the expected location or source links before retrying.",
      ),
    ).toBeInTheDocument();
    expect(screen.queryByText(/sourceLocationChanged/)).not.toBeInTheDocument();
  });
  it("can hide an unknown submission without unlocking it and continue using the panel", async () => {
    api.create.mockRejectedValue(new TypeError("Disconnected"));
    render(<ResourceMovePanel />);
    fireEvent.click(await screen.findByRole("button", { name: "Move selected (1)" }));
    const confirm = await screen.findByRole("dialog");

    await waitFor(() =>
      expect(within(confirm).getByRole("button", { name: "Confirm move 1" })).toBeEnabled(),
    );
    fireEvent.click(within(confirm).getByRole("button", { name: "Confirm move 1" }));
    fireEvent.click(
      await screen.findByRole("button", { name: "Hide for now; keep checking later" }),
    );
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(useResourceMovePanelStore.getState().pendingDrafts).toHaveLength(1);
    expect(
      useResourceMovePanelStore
        .getState()
        .reservations.some((r) => r.phase === "unknown" && r.resourceIds.includes(1)),
    ).toBe(true);
    expect(screen.getByRole("button", { name: "Unconfirmed submissions: /target" })).toBeEnabled();
  });
  it("requires explicit acknowledgement of manual full source restoration before unlocking legacy recovery", async () => {
    const batch: MoveBatch = {
      batchId: "legacy",
      destDir: "/target",
      status: "needsRecovery",
      percentage: 0,
      canCancel: false,
      counts: { total: 1, succeeded: 0, failed: 1, cancelled: 0, skipped: 0, waiting: 0 },
      records: [
        {
          id: 11,
          resourceId: 1,
          sourcePath: "/original/complete-resource",
          destPath: "/target/resource",
          status: 8,
          conflictKind: "legacyRecovery",
          conflictVersion: 6,
          canOverwrite: false,
        },
      ],
    };

    api.resolve.mockResolvedValue({});
    render(<MoveTaskCard batch={batch} />);
    fireEvent.click(screen.getByRole("button", { name: "Confirm source restored…" }));
    const confirm = screen.getByRole("dialog", {
      name: "Confirm complete resource restored to source",
    });

    expect(within(confirm).getByText("/original/complete-resource")).toBeVisible();
    const submit = within(confirm).getByRole("button", {
      name: "Verify restored source and release reservation",
    });

    expect(submit).toBeDisabled();
    fireEvent.click(submit);
    expect(api.resolve).not.toHaveBeenCalled();
    fireEvent.click(
      within(confirm).getByRole("checkbox", {
        name: "I have manually restored the complete resource to this original source path.",
      }),
    );
    fireEvent.click(submit);
    await waitFor(() => expect(api.resolve).toHaveBeenCalledWith(11, "restoreSource", "once", 6));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
  it("does not offer manual-source release for recovery backed by a move journal", () => {
    const batch: MoveBatch = {
      batchId: "owned",
      destDir: "/target",
      status: "needsRecovery",
      percentage: 0,
      counts: { total: 1, succeeded: 0, failed: 1, cancelled: 0, skipped: 0, waiting: 0 },
      records: [
        {
          id: 12,
          resourceId: 1,
          sourcePath: "/source",
          destPath: "/target/source",
          status: 8,
          conflictKind: "journalRecovery",
        },
      ],
    };

    render(<MoveTaskCard batch={batch} />);
    expect(
      screen.queryByRole("button", { name: "Confirm source restored…" }),
    ).not.toBeInTheDocument();
  });
  it("requires explicit confirmation of source files and platform links before releasing a missing legacy source plan", async () => {
    api.resolve.mockResolvedValue({});
    render(
      <MoveTaskCard
        batch={{
          batchId: "legacy-source-plan",
          destDir: "/target",
          status: "needsRecovery",
          canRetry: false,
          counts: { total: 1, succeeded: 0, failed: 1, cancelled: 0, skipped: 0, waiting: 0 },
          records: [
            {
              id: 13,
              resourceId: 1,
              sourcePath: "/source",
              destPath: "/target/source",
              status: 8,
              errorCode: "legacySourcePlanMissing",
              error: "legacySourcePlanMissing",
              conflictKind: "legacySourcePlanMissing",
              conflictVersion: 8,
            },
          ],
        }}
      />,
    );

    expect(screen.getByText(/This move began before the upgrade/)).toBeVisible();
    expect(screen.getByText(/Retrying cannot rebuild the missing plan/)).toHaveTextContent(
      "The resource remains reserved.",
    );
    expect(screen.queryByRole("button", { name: "Retry eligible items" })).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Confirm source restored…" }),
    ).not.toBeInTheDocument();
    fireEvent.click(
      screen.getByRole("button", { name: "Confirm source and platform links restored…" }),
    );
    const dialog = screen.getByRole("dialog", {
      name: "Confirm source files and platform links restored",
    });
    const confirm = within(dialog).getByRole("button", {
      name: "Verify restored source and platform links, then release reservation",
    });

    expect(within(dialog).getByText(/copying the folder alone is not enough/)).toBeVisible();
    expect(confirm).toBeDisabled();
    fireEvent.click(confirm);
    expect(api.resolve).not.toHaveBeenCalled();
    fireEvent.click(
      within(dialog).getByRole("checkbox", {
        name: "I have restored the complete resource to its original path and verified that its platform source links point to that location.",
      }),
    );
    fireEvent.click(confirm);
    await waitFor(() => expect(api.resolve).toHaveBeenCalledWith(13, "restoreSource", "once", 8));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });
  it("resolves conflicts through the backend with version and chosen scope, then reloads panel policy", async () => {
    const batch: MoveBatch = {
      batchId: "b",
      destDir: "/target",
      status: "waiting",
      percentage: 30,
      canCancel: true,
      counts: { total: 1, succeeded: 0, failed: 0, cancelled: 0, skipped: 0, waiting: 1 },
      records: [
        {
          id: 7,
          resourceId: 1,
          sourcePath: "/a",
          destPath: "/target/a",
          status: 7,
          canOverwrite: true,
          conflictVersion: 4,
        },
      ],
    };

    api.resolve.mockResolvedValue({});
    api.options.mockResolvedValue({ ...options, revision: 2, autoOverwrite: true });
    render(<MoveTaskCard batch={batch} />);
    fireEvent.click(screen.getByRole("button", { name: "This conflict only" }));
    fireEvent.click(await screen.findByRole("option", { name: "All panel tasks" }));
    fireEvent.click(screen.getByRole("button", { name: "Overwrite and continue" }));
    await waitFor(() => expect(api.resolve).toHaveBeenCalledWith(7, "overwrite", "panel", 4));
    await waitFor(() =>
      expect(useResourceMovePanelStore.getState().options.autoOverwrite).toBe(true),
    );
  });
});
