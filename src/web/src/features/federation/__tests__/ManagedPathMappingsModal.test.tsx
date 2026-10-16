import type { ManagedServer } from "../types";
import type * as ClientApiModule from "@/core/clientApi";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ManagedPathMappingsModal, {
  loadManagedPathMappingTarget,
} from "../components/ManagedPathMappingsModal";
import { readMappingDraft, writeMappingDraft } from "../components/managedMappingDrafts";
import { managedServerApi } from "../serverApi";
import { FederationError } from "../transport";
import { PATH_MAPPINGS_SAVED_EVENT } from "../managedPathMappingTarget";

import { clientApi, ClientApiError } from "@/core/clientApi";
import { ManagedServerState } from "@/sdk/constants";
import { nestedWindowModalClassNames, windowModalClassNames } from "@/components/Window/layers";

const fixture = vi.hoisted(() => ({ portal: vi.fn(), modal: vi.fn() }));

vi.mock("../serverApi", () => ({
  managedServerApi: { list: vi.fn(), mappingTree: vi.fn(), setPathMappings: vi.fn() },
}));
vi.mock("@/core/clientApi", async (original) => ({
  ...(await original<typeof ClientApiModule>()),
  clientApi: { status: vi.fn(), mappingTree: vi.fn(), setPathMappings: vi.fn() },
}));
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: fixture.portal }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
vi.mock("@/components/bakaui", () => ({
  Modal: (props: any) => {
    fixture.modal(props);
    const { children, title, visible, footer, onClose } = props;

    return visible ? (
      <div role="dialog">
        <h1>{title}</h1>
        {children}
        <button disabled={footer?.cancelProps?.isDisabled} onClick={onClose}>
          close
        </button>
      </div>
    ) : null;
  },
}));
vi.mock("../components/common", () => ({
  buttonClass: "",
  fieldClass: "",
  primaryClass: "",
  ErrorNotice: ({ error, onRetry }: any) =>
    error ? (
      <div role="alert">
        {error.code || error.message}
        <button onClick={onRetry}>retry</button>
      </div>
    ) : null,
}));

const server = (id = "source"): ManagedServer => ({
  serverId: id,
  name: id,
  address: "http://source",
  pairedAt: "2026-10-16",
  pathMappings: [{ serverPath: "/Media", localPath: "/saved" }],
  state: ManagedServerState.Online,
});
const tree = { libraryEpoch: "epoch", nodes: [] };
const relayStatus = (id = "source") => ({
  activeServerId: id,
  servers: [
    {
      serverId: id,
      serverName: id,
      pairedAt: "2026-10-16",
      pathMappings: server().pathMappings,
      isActive: true,
    },
  ],
});

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(managedServerApi.list).mockResolvedValue({
    available: true,
    servers: [server()],
    requests: [],
  });
  vi.mocked(managedServerApi.mappingTree).mockResolvedValue(tree);
  vi.mocked(managedServerApi.setPathMappings).mockResolvedValue({ changed: true });
  vi.mocked(clientApi.status).mockResolvedValue(relayStatus() as any);
  vi.mocked(clientApi.mappingTree).mockResolvedValue(tree);
  vi.mocked(clientApi.setPathMappings).mockResolvedValue({ changed: true });
});
afterEach(cleanup);

describe("shared path mapping dialog", () => {
  it("opens above floating players and gives its nested local folder picker a higher layer", async () => {
    render(<ManagedPathMappingsModal target={{ kind: "manager", serverId: "source" }} />);
    await screen.findByDisplayValue("/saved");
    expect(fixture.modal.mock.lastCall![0].classNames).toBe(windowModalClassNames);
    fireEvent.click(screen.getByLabelText("federation.managedMappings.chooseLocal"));
    expect(fixture.portal).toHaveBeenCalledWith(
      expect.any(Function),
      expect.objectContaining({
        targetType: "folder",
        startPath: "/saved",
        modalClassNames: nestedWindowModalClassNames,
      }),
    );
    act(() => fixture.portal.mock.lastCall![1].onSelected({ path: "/picked" }));
    expect(screen.getByDisplayValue("/picked")).toBeInTheDocument();
  });
  it("edits live manager mappings in place and notifies playback only after a successful CAS save", async () => {
    const onSaved = vi.fn();
    const listener = vi.fn();
    const location = window.location.href;

    window.addEventListener(PATH_MAPPINGS_SAVED_EVENT, listener);
    try {
      render(
        <ManagedPathMappingsModal
          target={{ kind: "manager", serverId: "source" }}
          onSaved={onSaved}
        />,
      );
      const input = await screen.findByDisplayValue("/saved");

      expect(await screen.findByText("federation.managedMappings.tree.empty")).toBeInTheDocument();
      expect(managedServerApi.mappingTree).toHaveBeenCalledTimes(1);
      fireEvent.change(input, { target: { value: "/local" } });
      fireEvent.click(screen.getByText("federation.servers.mappings.save"));
      await waitFor(() =>
        expect(onSaved).toHaveBeenCalledWith([{ serverPath: "/Media", localPath: "/local" }]),
      );
      expect(managedServerApi.setPathMappings).toHaveBeenCalledWith(
        "source",
        [{ serverPath: "/Media", localPath: "/local" }],
        server().pathMappings,
      );
      expect(listener).toHaveBeenCalledWith(
        expect.objectContaining({ detail: { kind: "manager", serverId: "source" } }),
      );
      expect(readMappingDraft("source")).toBeUndefined();
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(window.location.href).toBe(location);
      expect(clientApi.setPathMappings).not.toHaveBeenCalled();
    } finally {
      window.removeEventListener(PATH_MAPPINGS_SAVED_EVENT, listener);
    }
  });

  it("uses only the active relay connection and does not offer the upstream filesystem as a local picker", async () => {
    render(<ManagedPathMappingsModal target={{ kind: "relay", serverId: "source" }} />);
    const input = await screen.findByDisplayValue("/saved");

    expect(screen.getByText("federation.managedMappings.relayLocalPath")).toBeInTheDocument();
    expect(screen.queryByLabelText("federation.managedMappings.chooseLocal")).toBeNull();
    expect(clientApi.mappingTree).toHaveBeenCalledWith(
      "source",
      { parent: undefined, cursor: undefined, libraryEpoch: undefined },
      expect.any(AbortSignal),
    );
    expect(managedServerApi.mappingTree).not.toHaveBeenCalled();
    fireEvent.change(input, { target: { value: "Z:\\Media" } });
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    await waitFor(() =>
      expect(clientApi.setPathMappings).toHaveBeenCalledWith(
        "source",
        [{ serverPath: "/Media", localPath: "Z:\\Media" }],
        server().pathMappings,
      ),
    );
    expect(managedServerApi.list).not.toHaveBeenCalled();
    expect(managedServerApi.setPathMappings).not.toHaveBeenCalled();
  });

  it("does not expose or edit another connection from a relay", async () => {
    await expect(
      loadManagedPathMappingTarget({ kind: "relay", serverId: "other" }),
    ).rejects.toMatchObject({ code: "UnknownServer" });
    render(<ManagedPathMappingsModal target={{ kind: "relay", serverId: "other" }} />);
    expect(await screen.findByText("UnknownServer")).toBeInTheDocument();
    expect(screen.queryByText("federation.servers.mappings.save")).toBeNull();
    expect(clientApi.mappingTree).not.toHaveBeenCalled();
  });

  it.each(["manager", "relay"] as const)(
    "retains the draft when the %s connection disappears before saving",
    async (kind) => {
      const onSaved = vi.fn();

      vi.mocked(managedServerApi.setPathMappings).mockResolvedValue({ changed: false });
      vi.mocked(clientApi.setPathMappings).mockResolvedValue({ changed: false });
      render(<ManagedPathMappingsModal target={{ kind, serverId: "source" }} onSaved={onSaved} />);
      fireEvent.change(await screen.findByDisplayValue("/saved"), {
        target: { value: "/keep-my-draft" },
      });
      fireEvent.click(screen.getByText("federation.servers.mappings.save"));
      expect(await screen.findByText("UnknownServer")).toBeInTheDocument();
      expect(onSaved).not.toHaveBeenCalled();
      expect(screen.getByRole("dialog")).toBeInTheDocument();
      expect(readMappingDraft("source")?.mappings[0]?.localPath).toBe("/keep-my-draft");
    },
  );

  it("restores the existing device-management draft and clears it only after applying", async () => {
    writeMappingDraft(
      server(),
      [{ serverPath: "/Media", localPath: "/draft" }],
      server().pathMappings,
    );
    render(<ManagedPathMappingsModal target={{ kind: "manager", serverId: "source" }} />);
    fireEvent.click(await screen.findByText("federation.managedMappings.draft.restore"));
    expect(screen.getByDisplayValue("/draft")).toBeInTheDocument();
    expect(managedServerApi.setPathMappings).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    await waitFor(() => expect(readMappingDraft("source")).toBeUndefined());
  });

  it("keeps failed relay edits open and reloads a concurrent change before explicit replacement", async () => {
    const onSaved = vi.fn();

    render(
      <ManagedPathMappingsModal target={{ kind: "relay", serverId: "source" }} onSaved={onSaved} />,
    );
    fireEvent.change(await screen.findByDisplayValue("/saved"), { target: { value: "/mine" } });
    const fresh = {
      ...relayStatus(),
      servers: [
        {
          ...relayStatus().servers[0]!,
          pathMappings: [{ serverPath: "/Media", localPath: "/elsewhere" }],
        },
      ],
    };

    vi.mocked(clientApi.status).mockResolvedValue(fresh as any);
    vi.mocked(clientApi.setPathMappings).mockRejectedValueOnce(
      new ClientApiError("changed", 409, "PathMappingsChanged"),
    );
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    expect(await screen.findByText("PathMappingsChanged")).toBeInTheDocument();
    expect(onSaved).not.toHaveBeenCalled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByDisplayValue("/mine")).toBeInTheDocument();
    expect(readMappingDraft("source")?.mappings[0]?.localPath).toBe("/mine");
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    expect(screen.getByText("/Media → /elsewhere")).toBeInTheDocument();
    expect(clientApi.setPathMappings).toHaveBeenCalledTimes(1);
    fireEvent.click(screen.getByText("federation.managedMappings.replace"));
    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1));
    expect(clientApi.setPathMappings).toHaveBeenLastCalledWith(
      "source",
      [{ serverPath: "/Media", localPath: "/mine" }],
      fresh.servers[0]!.pathMappings,
    );
  });

  it("aborts a stale snapshot load and never renders the previous target in a new dialog", async () => {
    let finish!: (value: any) => void;

    vi.mocked(managedServerApi.list).mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const view = render(
      <ManagedPathMappingsModal target={{ kind: "manager", serverId: "source" }} />,
    );
    const firstSignal = vi.mocked(managedServerApi.list).mock.calls[0]![1]!;

    vi.mocked(managedServerApi.list).mockResolvedValue({
      available: true,
      servers: [server("other")],
      requests: [],
    });
    view.rerender(<ManagedPathMappingsModal target={{ kind: "manager", serverId: "other" }} />);
    expect(firstSignal.aborted).toBe(true);
    expect(await screen.findByDisplayValue("/saved")).toBeInTheDocument();
    await act(async () =>
      finish({
        available: true,
        servers: [{ ...server(), pathMappings: [{ serverPath: "/old", localPath: "/stale" }] }],
        requests: [],
      }),
    );
    expect(screen.queryByDisplayValue("/stale")).toBeNull();
  });

  it("offers retry after a snapshot failure without inventing an empty mapping list", async () => {
    vi.mocked(managedServerApi.list).mockRejectedValueOnce(
      new FederationError("Unavailable", "", 503),
    );
    render(<ManagedPathMappingsModal target={{ kind: "manager", serverId: "source" }} />);
    expect(await screen.findByText("Unavailable")).toBeInTheDocument();
    expect(screen.queryByText("federation.servers.mappings.add")).toBeNull();
    fireEvent.click(screen.getByText("retry"));
    expect(await screen.findByDisplayValue("/saved")).toBeInTheDocument();
  });
});
