import type { MoveBatch } from "../types";

import React, { useEffect } from "react";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { Link, MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const api = vi.hoisted(() => ({
  context: vi.fn(),
  options: vi.fn(),
  saveOptions: vi.fn(),
  batches: vi.fn(),
  activeBatches: vi.fn(),
  batch: vi.fn(),
  preview: vi.fn(),
  create: vi.fn(),
}));

vi.mock("../api", () => ({ movePanelApi: api, MoveRequestRejected: class extends Error {} }));
vi.mock("@/config/env", () => ({ default: { apiEndpoint: "/api" } }));
vi.mock("@/sdk/BApi", () => ({
  default: {
    mediaLibraryV2: { getAllMediaLibraryV2: vi.fn().mockResolvedValue({ data: [] }) },
    pathMark: {
      getAllPathMarks: vi.fn().mockResolvedValue({ data: [] }),
      getAllPathMarkPaths: vi.fn().mockResolvedValue({ data: [] }),
    },
    file: { getIwFsEntry: vi.fn().mockResolvedValue({ data: { path: "/target", type: 100 } }) },
  },
}));
vi.mock("@/components/FileExplorer", () => ({ FileExplorer: () => <div>Folder picker</div> }));
vi.mock("@/components/bakaui", async () => {
  const {
    Button,
    Checkbox,
    Chip,
    Input,
    Progress,
    Radio,
    RadioGroup,
    Select,
    SelectItem,
    Tooltip,
  } = await import("@heroui/react");

  return {
    Button,
    Checkbox,
    Chip,
    Input,
    Progress,
    Radio,
    RadioGroup,
    Tooltip,
    Select: ({ dataSource, ...props }: any) => (
      <Select {...props}>
        {(dataSource ?? []).map((item: any) => (
          <SelectItem key={item.value}>{item.label}</SelectItem>
        ))}
      </Select>
    ),
    Modal: ({ children, title, visible }: any) =>
      visible ? (
        <div aria-label={title} role="dialog">
          {children}
        </div>
      ) : null,
    Spinner: () => <span>Loading</span>,
  };
});
vi.mock("react-rnd", () => ({ Rnd: ({ children }: any) => <div>{children}</div> }));

import ResourceMovePanelRoute from "../ResourceMovePanelRoute";
import { MoveRequestRejected } from "../api";
import { ResourceMovePanelDock } from "../index";

import {
  openMovePanel,
  prepareMove,
  setMovePanelContext,
  submitMoveDraft,
  useResourceMovePanelStore,
} from "@/stores/resourceMovePanel";

const initial = useResourceMovePanelStore.getState();
const sourceContext = { nodeId: "this-server", libraryEpoch: "this-library" };
const destination = { id: "dest", path: "/target", scope: "global" as const, order: 0 };
const options = { revision: 1, destinations: [destination], autoOverwrite: false };
const context = {
  tabId: "a",
  resources: [{ id: 1, path: "/source/a" }],
  selectedResources: [{ id: 1, path: "/source/a" }],
};
const preview = {
  previewFingerprint: "verified",
  items: [{ resourceId: 1, sourcePath: "/source/a", destPath: "/target/a", effects: [] }],
};

function ResourcePage({ initializeContext }: { initializeContext: boolean }) {
  useEffect(() => {
    if (initializeContext) setMovePanelContext({ ...context, selectedResources: [] });
  }, [initializeContext]);

  return <ResourceMovePanelDock />;
}

function Navigation() {
  const location = useLocation();

  return (
    <>
      <output data-testid="location">{location.pathname + location.search}</output>
      <Link to="/other">Leave resources</Link>
      <Link to="/resource">Return to resources</Link>
      <Link to="/resource?tab=b">Change resource tab</Link>
      <button onClick={() => openMovePanel()}>Open move panel</button>
      <button onClick={() => openMovePanel({ resources: [{ id: 9, path: "/elsewhere/book" }] })}>
        Move external resource
      </button>
    </>
  );
}

async function renderPage(path = "/resource", initializeContext = false) {
  let view!: ReturnType<typeof render>;

  await act(async () => {
    view = render(
      <MemoryRouter initialEntries={[path]}>
        <ResourceMovePanelRoute />
        <Navigation />
        <Routes>
          <Route
            element={<ResourcePage initializeContext={initializeContext} />}
            path="/resource"
          />
          <Route element={<div>Other page</div>} path="*" />
        </Routes>
      </MemoryRouter>,
    );
  });

  return view;
}

beforeEach(() => {
  vi.clearAllMocks();
  Object.values(api).forEach((mock) => mock.mockReset());
  localStorage.clear();
  api.context.mockResolvedValue(sourceContext);
  api.options.mockResolvedValue(options);
  api.batches.mockResolvedValue([]);
  api.activeBatches.mockResolvedValue([]);
  api.preview.mockResolvedValue(preview);
  useResourceMovePanelStore.setState(
    {
      ...initial,
      sourceContext,
      initialized: true,
      options,
      context,
      open: true,
      panelEnabled: true,
    },
    true,
  );
});
afterEach(cleanup);

describe("resource move panel page scope", () => {
  it("uses router matching for the resource route without including similarly named pages", async () => {
    const view = await renderPage("/RESOURCE/");

    expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
    await waitFor(() => expect(api.options).toHaveBeenCalledTimes(1));
    view.unmount();
    useResourceMovePanelStore.setState({ panelEnabled: true, open: true });
    await renderPage("/resource-profile");
    expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
    expect(useResourceMovePanelStore.getState().panelEnabled).toBe(false);
  });

  it.each(["floating", "docked", "minimized"] as const)(
    "closes %s presentation and clears transient inputs when leaving; returning stays closed",
    async (presentation) => {
      useResourceMovePanelStore.setState({
        mode: presentation === "docked" ? "docked" : "floating",
        minimized: presentation === "minimized",
        open: presentation !== "minimized",
        dragging: true,
        explicitPayload: { resources: [{ id: 9, path: "/explicit" }] },
      });
      await renderPage();
      await waitFor(() => expect(api.options).toHaveBeenCalledTimes(1));
      if (presentation === "minimized")
        expect(screen.getByRole("button", { name: "Expand move panel" })).toBeVisible();
      else expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
      if (presentation === "docked")
        expect(document.querySelector(".move-panel-dock")).not.toBeNull();

      fireEvent.click(screen.getByText("Leave resources"));

      expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
      expect(useResourceMovePanelStore.getState()).toMatchObject({
        panelEnabled: false,
        open: false,
        minimized: false,
        dragging: false,
        context: { resources: [], selectedResources: [] },
      });
      expect(useResourceMovePanelStore.getState().explicitPayload).toBeUndefined();
      fireEvent.click(screen.getByText("Return to resources"));
      expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
      await act(async () => {
        fireEvent.click(screen.getByText("Open move panel"));
      });
      expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
    },
  );

  it("preserves the open panel and draft across resource query/tab changes", async () => {
    await renderPage();
    await act(async () => prepareMove(destination));
    const draft = useResourceMovePanelStore.getState().draft;

    expect(screen.getByRole("dialog")).toBeVisible();
    fireEvent.click(screen.getByText("Change resource tab"));
    expect(screen.getByTestId("location")).toHaveTextContent("/resource?tab=b");
    expect(useResourceMovePanelStore.getState().draft).toBe(draft);
    expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
    expect(screen.getByRole("dialog")).toBeVisible();
  });

  it("cancels an unsubmitted confirmation and ignores its late preview completion", async () => {
    let finish!: (value: typeof preview) => void;

    api.preview.mockImplementation(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    await renderPage();
    let preparing!: Promise<void>;

    act(() => {
      preparing = prepareMove(destination);
    });
    await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(1));
    expect(screen.getByRole("dialog")).toBeVisible();
    fireEvent.click(screen.getByText("Leave resources"));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
    expect(useResourceMovePanelStore.getState().reservations).toEqual([]);
    await act(async () => {
      finish(preview);
      await preparing;
    });
    fireEvent.click(screen.getByText("Return to resources"));
    expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
    expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
  });

  it("retains an in-flight ambiguous request and its reservations without reopening on return", async () => {
    let reject!: (error: Error) => void;

    api.create.mockImplementation(
      () =>
        new Promise((_, fail) => {
          reject = fail;
        }),
    );
    await renderPage();
    await act(async () => prepareMove(destination));
    let submitting!: Promise<void>;

    act(() => {
      submitting = submitMoveDraft();
    });
    await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
    const request = useResourceMovePanelStore.getState().draft!.request;

    fireEvent.click(screen.getByText("Leave resources"));
    expect(useResourceMovePanelStore.getState().draft!.request).toBe(request);
    expect(useResourceMovePanelStore.getState().reservations[0].phase).toBe("submitting");
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await act(async () => {
      reject(new Error("Connection lost"));
      await submitting;
    });
    expect(useResourceMovePanelStore.getState().draft).toMatchObject({ phase: "unknown", request });
    expect(useResourceMovePanelStore.getState().reservations[0].phase).toBe("unknown");
    expect(
      localStorage.getItem(Object.keys(localStorage).find((key) => key.includes(":pending:"))!),
    ).toContain(request!.idempotencyKey);
    fireEvent.click(screen.getByText("Return to resources"));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByText("Open move panel"));
    });
    expect(screen.getByRole("dialog")).toBeVisible();
  });

  it("keeps background task synchronization and reservations alive on other pages", async () => {
    const batch: MoveBatch = {
      batchId: "running",
      status: "running",
      destDir: "/target",
      lockedResourceIds: [3],
      reservedPaths: ["/source/three"],
      counts: { total: 1, succeeded: 0, failed: 0, cancelled: 0, skipped: 0, waiting: 0 },
      records: [],
    };

    api.batches.mockResolvedValue([batch]);
    api.activeBatches.mockResolvedValue([batch]);
    await renderPage("/other");
    await waitFor(() => expect(useResourceMovePanelStore.getState().batches).toEqual([batch]));
    expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
    expect(useResourceMovePanelStore.getState().reservations).toMatchObject([
      { phase: "task", batchId: "running", resourceIds: [3] },
    ]);
    fireEvent(window, new Event("online"));
    await waitFor(() => expect(api.activeBatches).toHaveBeenCalledTimes(2));
    expect(screen.getByTestId("location")).toHaveTextContent("/other");
    expect(useResourceMovePanelStore.getState().panelEnabled).toBe(false);
  });

  it.each(["previewChanged", "resourceLocked"])(
    "discards a late definitive %s rejection after leaving instead of restoring an unsent draft",
    async (reason) => {
      let reject!: (error: Error) => void;

      api.create.mockImplementation(
        () =>
          new Promise((_, fail) => {
            reject = fail;
          }),
      );
      await renderPage();
      await act(async () => prepareMove(destination));
      let submitting!: Promise<void>;

      act(() => {
        submitting = submitMoveDraft();
      });
      await waitFor(() => expect(api.create).toHaveBeenCalledTimes(1));
      fireEvent.click(screen.getByText("Leave resources"));
      await act(async () => {
        reject(new MoveRequestRejected(reason));
        await submitting;
      });

      expect(api.preview).toHaveBeenCalledTimes(1);
      expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
      expect(useResourceMovePanelStore.getState().reservations).toEqual([]);
      await act(async () => {
        fireEvent.click(screen.getByText("Open move panel"));
      });
      expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    },
  );

  it("ignores a late replacement preview after a rejected submission has left the page", async () => {
    await renderPage();
    await act(async () => prepareMove(destination));
    let finish!: (value: typeof preview) => void;

    api.create.mockRejectedValue(new MoveRequestRejected("previewChanged"));
    api.preview.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          finish = resolve;
        }),
    );
    let submitting!: Promise<void>;

    act(() => {
      submitting = submitMoveDraft();
    });
    await waitFor(() => expect(api.preview).toHaveBeenCalledTimes(2));
    fireEvent.click(screen.getByText("Leave resources"));
    await act(async () => {
      finish(preview);
      await submitting;
    });
    expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
    expect(useResourceMovePanelStore.getState().reservations).toEqual([]);
  });

  it.each([false, true])(
    "handles a late context refusal after leaving, preserving a previous ambiguous request: %s",
    async (retryUnknown) => {
      await renderPage();
      await act(async () => prepareMove(destination));
      if (retryUnknown) {
        api.create.mockRejectedValue(new Error("Connection lost"));
        await act(async () => submitMoveDraft());
      }
      let reject!: (error: Error) => void;

      api.context.mockImplementationOnce(
        () =>
          new Promise((_, fail) => {
            reject = fail;
          }),
      );
      let submitting!: Promise<void>;

      act(() => {
        submitting = submitMoveDraft();
      });
      const request = useResourceMovePanelStore.getState().draft!.request;

      fireEvent.click(screen.getByText("Leave resources"));
      await act(async () => {
        reject(new Error("Context unavailable"));
        await submitting;
      });
      if (retryUnknown) {
        expect(useResourceMovePanelStore.getState().draft).toMatchObject({
          request,
          phase: "unknown",
        });
        expect(useResourceMovePanelStore.getState().reservations[0].phase).toBe("unknown");
      } else {
        expect(useResourceMovePanelStore.getState().draft).toBeUndefined();
        expect(useResourceMovePanelStore.getState().reservations).toEqual([]);
      }
      expect(api.create).toHaveBeenCalledTimes(retryUnknown ? 1 : 0);
      fireEvent.click(screen.getByText("Return to resources"));
      expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    },
  );

  it("opens explicitly from another page and preserves the payload through initial context publication", async () => {
    await renderPage("/other", true);
    await waitFor(() => expect(api.options).toHaveBeenCalledTimes(1));
    await act(async () => {
      fireEvent.click(screen.getByText("Move external resource"));
    });

    expect(screen.getByTestId("location")).toHaveTextContent("/resource");
    expect(screen.getByRole("region", { name: "Move resources" })).toBeVisible();
    expect(useResourceMovePanelStore.getState().explicitPayload).toMatchObject({
      resources: [{ id: 9, path: "/elsewhere/book" }],
      sourceContext,
    });
    act(() => setMovePanelContext({ ...context, tabId: "b" }));
    expect(useResourceMovePanelStore.getState().explicitPayload).toBeUndefined();
  });

  it("clears the page view when its layout unmounts while retaining submitted recovery", async () => {
    api.create.mockRejectedValue(new Error("Connection lost"));
    const view = await renderPage();

    await act(async () => {
      await prepareMove(destination);
      await submitMoveDraft();
    });
    const request = useResourceMovePanelStore.getState().draft!.request;

    view.unmount();
    expect(useResourceMovePanelStore.getState().panelEnabled).toBe(false);
    expect(useResourceMovePanelStore.getState().draft!.request).toBe(request);
    expect(useResourceMovePanelStore.getState().reservations[0].phase).toBe("unknown");
    expect(document.querySelector("[data-resource-move-panel]")).toBeNull();
  });
});
