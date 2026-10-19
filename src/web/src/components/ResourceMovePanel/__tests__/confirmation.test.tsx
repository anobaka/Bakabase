import type { MoveDraft, MovePreview } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const { api } = vi.hoisted(() => ({
  api: {
    context: vi.fn(),
    create: vi.fn(),
    batch: vi.fn(),
    options: vi.fn(),
    batches: vi.fn(),
    activeBatches: vi.fn(),
  },
}));

vi.mock("../api", () => ({ movePanelApi: api, MoveRequestRejected: class extends Error {} }));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "/api" } }));
vi.mock("react-i18next", async () => {
  const { default: messages } = await import("@/locales/en/components/resourceMove.json");

  return {
    useTranslation: () => ({
      t: (key: string) => messages[key as keyof typeof messages] ?? key,
      i18n: { language: "en" },
    }),
  };
});
vi.mock("@/components/bakaui", async () => {
  const { Button, Checkbox, Radio, RadioGroup } = await import("@heroui/react");

  return {
    Button,
    Checkbox,
    Radio,
    RadioGroup,
    Modal: ({
      children,
      title,
      visible,
    }: {
      children?: React.ReactNode;
      title?: string;
      visible?: boolean;
    }) =>
      visible ? (
        <div aria-label={title} role="dialog">
          {children}
        </div>
      ) : null,
    Spinner: () => <span>Loading</span>,
  };
});

import MoveConfirmation from "../MoveConfirmation";

import { useResourceMovePanelStore } from "@/stores/resourceMovePanel";

const initial = useResourceMovePanelStore.getState();
const sourceContext = { nodeId: "node", libraryEpoch: "epoch" };

function previewItem(id: number, conflict = false) {
  return {
    resourceId: id,
    sourcePath: `/source/${id}`,
    destPath: `/target/${id}`,
    effects: [],
    coveredResources: [],
    destConflict: conflict,
    ...(conflict ? { conflictKind: "destinationExists", canOverwrite: true } : {}),
  };
}

function show(preview: Partial<MovePreview>, extra: Partial<MoveDraft> = {}) {
  const fullPreview = {
    items: [previewItem(1, true), previewItem(2)],
    previewFingerprint: "confirmed",
    ...preview,
  } as MovePreview;
  const draft: MoveDraft = {
    id: "draft",
    phase: "ready",
    conflictPolicy: "inherit",
    destination: { id: "destination", path: "/target", scope: "global", order: 0 },
    payload: {
      sourceContext,
      resources: fullPreview.items.map((item) => ({
        id: item.resourceId,
        path: item.sourcePath,
        displayName: `Resource ${item.resourceId}`,
      })),
    },
    preview: fullPreview,
    ...extra,
  };

  useResourceMovePanelStore.setState({ draft });

  return render(<MoveConfirmation />);
}

beforeEach(() => {
  vi.clearAllMocks();
  api.context.mockResolvedValue(sourceContext);
  api.create.mockResolvedValue({ batchId: "accepted", skippedResourceCount: 0 });
  api.batch.mockResolvedValue(undefined);
  api.options.mockResolvedValue({ destinations: [], autoOverwrite: false });
  api.batches.mockResolvedValue([]);
  api.activeBatches.mockResolvedValue([]);
  useResourceMovePanelStore.setState({ ...initial, sourceContext }, true);
});
afterEach(cleanup);

describe("move confirmation conflict choices", () => {
  it("allows overwrite for an ordinary existing destination and submits the selected policy", async () => {
    show({});
    fireEvent.click(screen.getByRole("radio", { name: "Overwrite same-name files" }));
    expect(screen.getByRole("radio", { name: "Overwrite same-name files" })).toBeChecked();
    const confirm = screen.getByRole("button", { name: "Confirm move 2" });

    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.lastCall![0]).toMatchObject({
      conflictPolicy: "overwrite",
      resourceIds: [1, 2],
      expectedPreviewFingerprint: "confirmed",
    });
  });

  it("blocks shared batch destinations for overwrite and allows skipping every colliding source", async () => {
    show({
      items: [
        { ...previewItem(1), destPath: "/target/Same", conflictKind: "batchDestination" },
        { ...previewItem(2), destPath: "/target/same", conflictKind: "batchDestination" },
        previewItem(3),
      ],
      duplicateDestinationPaths: ["/target/Same"],
    });
    fireEvent.click(screen.getByRole("radio", { name: "Overwrite same-name files" }));
    expect(screen.getByRole("button", { name: "Confirm move 3" })).toBeDisabled();
    expect(
      screen.getByText(
        "Multiple selected resources map to the same destination. Overwrite cannot choose which source to keep.",
      ),
    ).toBeVisible();
    fireEvent.click(screen.getByRole("radio", { name: "Skip conflicting resources" }));
    expect(
      screen.getByText(
        "All sources sharing a destination in this batch will be skipped. Other resources will continue moving.",
      ),
    ).toBeVisible();
    expect(screen.getAllByText(/Will be skipped/)).toHaveLength(2);
    const confirm = screen.getByRole("button", { name: "Confirm move 3" });

    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.lastCall![0]).toMatchObject({
      conflictPolicy: "skip",
      resourceIds: [1, 2, 3],
    });
  });

  it("filters only the preview while keeping excluded reasons and the original submitted move set", async () => {
    show({
      excludedResources: [
        {
          resourceId: 9,
          displayName: "Unavailable book",
          path: "/not-moved",
          reasonCode: "sourceMissing",
        },
      ],
    });
    expect(screen.getByText("/source/2")).toBeVisible();
    fireEvent.click(screen.getByRole("checkbox", { name: "Show conflicts only" }));
    expect(screen.queryByText("/source/2")).not.toBeInTheDocument();
    expect(screen.getByText("/source/1")).toBeVisible();
    expect(screen.getByText("Unavailable book")).toBeVisible();
    fireEvent.click(screen.getByRole("checkbox", { name: "Show conflicts only" }));
    expect(screen.getByText("/source/2")).toBeVisible();
    fireEvent.click(screen.getByRole("checkbox", { name: "Show conflicts only" }));
    fireEvent.click(screen.getByRole("button", { name: "Confirm move 2" }));
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    expect(api.create.mock.lastCall![0]).toMatchObject({
      resourceIds: [1, 2],
      conflictPolicy: "inherit",
      expectedPreviewFingerprint: "confirmed",
    });
  });

  it("shows empty feedback for a conflict-free filtered preview without disabling confirmation", () => {
    show({ items: [previewItem(1), previewItem(2)] });
    fireEvent.click(screen.getByRole("checkbox", { name: "Show conflicts only" }));
    expect(
      screen.getByText(
        "No conflicts. This filter only changes the preview; the move selection is unchanged.",
      ),
    ).toBeVisible();
    expect(screen.queryByText("/source/1")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm move 2" })).toBeEnabled();
  });

  it("keeps an ambiguous submission's policy immutable while still allowing preview filtering", async () => {
    const view = show({});
    const draft = useResourceMovePanelStore.getState().draft!;

    act(() =>
      useResourceMovePanelStore.setState({
        draft: {
          ...draft,
          phase: "unknown",
          request: {
            resourceIds: [1, 2],
            resourceRefs: [
              { ...sourceContext, resourceId: 1 },
              { ...sourceContext, resourceId: 2 },
            ],
            destDir: "/target",
            origin: "move-panel",
            idempotencyKey: "same-request",
            conflictPolicy: "ask",
          },
          conflictPolicy: "ask",
        },
      }),
    );
    expect(screen.getByRole("radio", { name: "Skip conflicting resources" })).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox", { name: "Show conflicts only" }));
    expect(screen.queryByText("/source/2")).not.toBeInTheDocument();
    expect(useResourceMovePanelStore.getState().draft!.request!.conflictPolicy).toBe("ask");
    view.unmount();
  });
});
