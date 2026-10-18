import type { Resource as ResourceModel } from "@/core/models/Resource";

import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import Resource from "..";

import { ResourceSource, ResourceStatus } from "@/sdk/constants";
import {
  closeMovePanel,
  minimizeMovePanel,
  useResourceMovePanelStore,
} from "@/stores/resourceMovePanel";

const mocks = vi.hoisted(() => ({
  remote: false,
  createPortal: vi.fn(),
}));

vi.mock("@/features/management/resourceContext", () => ({
  ResourceOwnerProvider: ({ children }: any) => children,
  useResourceApi: () => ({}),
  useResourceOwner: () => (mocks.remote ? { serverId: "remote", isLocal: false } : undefined),
}));
vi.mock("@/features/management/useOwnerResourceDisplayOptions", () => ({
  useOwnerResourceDisplayOptions: () => ({}),
}));
vi.mock("@/features/management/useResourceCoverProperties", () => ({
  useResourceCoverProperties: () => ({}),
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: mocks.createPortal }),
}));
vi.mock("@/stores/options", () => ({
  useUiOptionsStore: (select: any) => select({ data: { resource: {} } }),
  useResourceOptionsStore: (select: any) => select({ data: {} }),
}));
vi.mock("@/stores/bTasks", () => ({
  useBTasksStore: (select: any) => select({}),
  selectResourceMovingTask: () => () => undefined,
}));
vi.mock("@/components/ResourceMovePanel/api", () => ({
  movePanelApi: {},
  MoveRequestRejected: class extends Error {},
}));
vi.mock("@/components/ResourceMovePanel/messages", () => ({
  useMoveReasonText: () => (reason: string) => reason,
}));
vi.mock("@/components/Resource/components/ResourceCover", async () => {
  const { forwardRef } = await import("react");

  return {
    default: forwardRef(({ onClick }: any, _ref) => (
      <button onClick={onClick}>Open resource</button>
    )),
  };
});
vi.mock("@/components/Resource/components/ContextMenu", () => ({
  default: ({ children, moveResourceIds }: any) => (
    <div data-move-ids={moveResourceIds.join(",")}>{children}</div>
  ),
}));
vi.mock("@/components/Resource/components/Operations", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/CoverPropertyValue", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/DetailModal", () => ({ default: () => null }));
vi.mock("@/components/Resource/components/HealthScoreDiagnosisModal", () => ({
  default: () => null,
}));
vi.mock("@/components/Resource/components/AcquisitionModal", () => ({ default: () => null }));
vi.mock("@/components/HealthScoreBadge", () => ({ default: () => null }));
vi.mock("@/components/StandardValue/ValueRenderer", () => ({ default: () => null }));
vi.mock("@/components/StandardValue/helpers", () => ({ convertFromApiValue: (v: any) => v }));
vi.mock("@/components/Resource/components/PlayControl", async () => {
  const { forwardRef } = await import("react");

  return { default: forwardRef(() => null) };
});
vi.mock("@/components/Resource/components/PlayControl/PlaybackButtons", () => ({
  ResourceCardPlaybackButton: () => null,
}));
vi.mock("@/components/Resource/components/ResourceSourceIcon", () => ({ default: () => null }));
vi.mock("@/components/BTask", () => ({ BTaskStopButton: () => null }));
vi.mock("@/components/utils", () => ({ autoBackgroundColor: () => "" }));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children }: any) => <button>{children}</button>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Link: ({ children }: any) => <span>{children}</span>,
  Spinner: () => null,
  Tooltip: ({ children }: any) => children,
}));

const initialState = useResourceMovePanelStore.getState();
const sourceContext = { nodeId: "local", libraryEpoch: "epoch-one" };
const resource = (id: number): ResourceModel =>
  ({
    id,
    path: `/library/${id}`,
    displayName: `Resource ${id}`,
    hasLocalPath: true,
    tags: [],
  }) as ResourceModel;
const payload = (id: number) => ({
  resources: [resource(id)],
  sourceTabId: "tab-one",
  sourceContext,
});

beforeEach(() => {
  vi.clearAllMocks();
  mocks.remote = false;
  useResourceMovePanelStore.setState({ ...initialState, sourceContext }, true);
});
afterEach(() => {
  cleanup();
  useResourceMovePanelStore.setState(initialState, true);
});

describe("pending resource move indication", () => {
  it("marks the explicit source without changing list selection or context-menu move targeting", () => {
    const onSelected = vi.fn();
    const { container } = render(
      <>
        <Resource
          resource={resource(1)}
          selectedResourceIds={[2]}
          sourceTabId="tab-one"
          onSelected={onSelected}
        />
        <Resource
          selected
          resource={resource(2)}
          selectedResourceIds={[2]}
          sourceTabId="tab-one"
          onSelected={onSelected}
        />
      </>,
    );

    act(() =>
      useResourceMovePanelStore.setState({ panelEnabled: true, explicitPayload: payload(1) }),
    );
    const source = container.querySelector('[data-id="1"]')!;
    const selected = container.querySelector('[data-id="2"]')!;

    expect(source).toHaveAttribute("data-move-source", "true");
    expect(source).toHaveClass("ring-warning/40");
    expect(source).not.toHaveClass("border-primary");
    expect(source.querySelector("[data-move-ids]")).toHaveAttribute("data-move-ids", "1");
    expect(selected).not.toHaveAttribute("data-move-source");
    expect(selected).toHaveClass("border-primary");
    expect(onSelected).not.toHaveBeenCalled();
    expect(screen.getByText("resource.pendingMove.label")).toBeInTheDocument();
    fireEvent.click(source, { shiftKey: true });
    expect(onSelected).toHaveBeenCalledExactlyOnceWith(1, true);
  });

  it("updates the marked resource with the payload and clears it on close or payload removal", () => {
    const { container } = render(
      <>
        <Resource resource={resource(1)} sourceTabId="tab-one" />
        <Resource resource={resource(2)} sourceTabId="tab-one" />
      </>,
    );
    const marked = () => container.querySelector("[data-move-source]");

    act(() =>
      useResourceMovePanelStore.setState({ panelEnabled: true, explicitPayload: payload(1) }),
    );
    expect(marked()).toHaveAttribute("data-id", "1");
    act(() => useResourceMovePanelStore.setState({ explicitPayload: payload(2) }));
    expect(marked()).toHaveAttribute("data-id", "2");
    act(() => minimizeMovePanel());
    expect(marked()).toHaveAttribute("data-id", "2");
    act(() => useResourceMovePanelStore.setState({ explicitPayload: undefined }));
    expect(marked()).toBeNull();
    act(() => useResourceMovePanelStore.setState({ explicitPayload: payload(1) }));
    expect(marked()).toHaveAttribute("data-id", "1");
    act(() => closeMovePanel());
    expect(marked()).toBeNull();
  });

  it.each(["foreignOwner", "foreignContext", "invalidContext", "differentPath", "differentTab"])(
    "does not mark a resource from %s",
    (mismatch) => {
      mocks.remote = mismatch === "foreignOwner";
      const explicitPayload = payload(1);

      if (mismatch === "foreignContext")
        explicitPayload.sourceContext = { ...sourceContext, nodeId: "other" };
      if (mismatch === "differentPath") explicitPayload.resources[0].path = "/other/resource";
      if (mismatch === "differentTab") explicitPayload.sourceTabId = "tab-two";
      useResourceMovePanelStore.setState({
        panelEnabled: true,
        explicitPayload,
        sourceContextInvalidated: mismatch === "invalidContext",
      });
      const { container } = render(<Resource resource={resource(1)} sourceTabId="tab-one" />);

      expect(container.querySelector("[data-move-source]")).toBeNull();
    },
  );
});

describe("missing local file indication", () => {
  it("labels an absent bound PathMark resource as files missing", () => {
    render(
      <Resource
        resource={{
          ...resource(1),
          status: ResourceStatus.Absent,
          sourceLinks: [{ source: ResourceSource.PathMark }] as ResourceModel["sourceLinks"],
        }}
      />,
    );

    expect(screen.getByText("resource.label.filesMissing")).toBeInTheDocument();
    expect(screen.queryByText("enum.resourceStatus.absent")).not.toBeInTheDocument();
  });

  it.each([[ResourceSource.Steam], [ResourceSource.PathMark, ResourceSource.Steam]])(
    "preserves source-availability wording for external source combination %j",
    (...sources) => {
      render(
        <Resource
          resource={{
            ...resource(1),
            status: ResourceStatus.Absent,
            sourceLinks: sources.map((source) => ({ source })) as ResourceModel["sourceLinks"],
          }}
        />,
      );

      expect(screen.getByText("enum.resourceStatus.absent")).toBeInTheDocument();
      expect(screen.queryByText("resource.label.filesMissing")).not.toBeInTheDocument();
    },
  );

  it("does not call an unmaterialized resource's files missing", () => {
    render(
      <Resource
        resource={{
          ...resource(1),
          path: undefined,
          hasLocalPath: false,
          status: ResourceStatus.Absent,
          sourceLinks: [{ source: ResourceSource.PathMark }] as ResourceModel["sourceLinks"],
        }}
      />,
    );

    expect(screen.getByText("resource.label.notMaterialized")).toBeInTheDocument();
    expect(screen.getByText("enum.resourceStatus.absent")).toBeInTheDocument();
    expect(screen.queryByText("resource.label.filesMissing")).not.toBeInTheDocument();
  });
});
