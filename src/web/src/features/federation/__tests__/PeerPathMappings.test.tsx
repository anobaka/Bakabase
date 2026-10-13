import type { Peer, MappingTreeNode } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import PeerPathMappings from "../components/PeerPathMappings";
import { mappedDirectoryPreview } from "../components/MappingTree";
import {
  clearMappingDraft,
  mappingDraftKey,
  readMappingDraft,
  writeMappingDraft,
} from "../components/mappingDrafts";
import { federationPeerApi } from "../peerApi";
import { FederationError } from "../transport";

vi.mock("../peerApi", () => ({
  federationPeerApi: { mappingTree: vi.fn(), mappingRoots: vi.fn() },
}));
const portals = vi.hoisted(() => vi.fn());

vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: portals }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));
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
const peer = (nodeId = "peer-a"): Peer => ({
  nodeId,
  label: nodeId,
  enabled: true,
  connectionState: "Online",
  pathMappings: [],
  outboundGrant: { grantId: `${nodeId}-grant`, revision: 1 },
});
const node = (sourceRootId: string, name: string, hasChildren = false): MappingTreeNode => ({
  sourceRootId,
  name,
  resourceCount: 10,
  hasChildren,
  browseToken: `token-${sourceRootId}`,
});
const show = (value = peer(), save = vi.fn().mockResolvedValue(true)) => ({
  save,
  view: render(<PeerPathMappings busy={false} peer={value} onSave={save} />),
});
const open = async () => {
  const details = screen.getByText("federation.mappings.title").closest("details")!;

  details.open = true;
  fireEvent(details, new Event("toggle"));
  await waitFor(() => expect(federationPeerApi.mappingTree).toHaveBeenCalled());
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(federationPeerApi.mappingTree).mockResolvedValue({
    nodes: [node("root", "Shared", true)],
  });
  vi.mocked(federationPeerApi.mappingRoots).mockResolvedValue([
    { sourceRootId: "legacy", name: "Old library" },
  ]);
});
afterEach(cleanup);

describe("lazy own-library mapping directory tree", () => {
  it("selects the target directory with the current Bakabase filesystem picker", async () => {
    show({ ...peer(), pathMappings: [{ sourceRootId: "source", localPath: "/old" }] });
    fireEvent.click(screen.getByLabelText("federation.mappings.chooseLocal"));
    const options = portals.mock.lastCall![1];

    expect(options).toMatchObject({
      targetType: "folder",
      startPath: "/old",
      defaultSelectedPath: "/old",
    });
    await act(async () => options.onSelected({ path: "/Volumes/Changed" }));
    expect(screen.getByDisplayValue("/Volumes/Changed")).toBeInTheDocument();
    expect(readMappingDraft("peer-a")?.mappings[0]?.localPath).toBe("/Volumes/Changed");
  });
  it("uses explicit MappingTreeUnavailable 501 fallback from a current coordinator", async () => {
    vi.mocked(federationPeerApi.mappingTree).mockRejectedValueOnce(
      new FederationError("MappingTreeUnavailable", "", 501),
    );
    show();
    await open();
    expect(await screen.findByText("Old library")).toBeInTheDocument();
    expect(federationPeerApi.mappingRoots).toHaveBeenCalledOnce();
  });

  it("ignores an old root response after refreshing the tree", async () => {
    let done!: (page: any) => void;

    vi.mocked(federationPeerApi.mappingTree).mockReturnValueOnce(
      new Promise((resolve) => {
        done = resolve;
      }),
    );
    show();
    await open();
    fireEvent.click(screen.getByText("federation.mappings.tree.refresh"));
    expect(await screen.findByText("Shared")).toBeInTheDocument();
    await act(async () => done({ nodes: [node("stale", "Stale response")] }));
    expect(screen.queryByText("Stale response")).toBeNull();
  });
  it("loads only the root page initially, children by parent token, and adds a child rule without auto-saving", async () => {
    const { save } = show();

    expect(federationPeerApi.mappingTree).not.toHaveBeenCalled();
    await open();
    expect(await screen.findByText("Shared")).toBeInTheDocument();
    expect(federationPeerApi.mappingTree).toHaveBeenCalledWith(
      "peer-a",
      {},
      expect.any(AbortSignal),
    );
    vi.mocked(federationPeerApi.mappingTree).mockResolvedValueOnce({
      nodes: [node("child", "Anime")],
    });
    fireEvent.click(screen.getByLabelText("federation.mappings.tree.expand"));
    expect(await screen.findByText("Anime")).toBeInTheDocument();
    expect(federationPeerApi.mappingTree).toHaveBeenLastCalledWith(
      "peer-a",
      { parent: "token-root", cursor: undefined },
      expect.any(AbortSignal),
    );
    fireEvent.click(screen.getAllByLabelText("federation.mappings.tree.select")[1]!);
    expect(screen.getByDisplayValue("child")).toBeInTheDocument();
    fireEvent.change(screen.getByPlaceholderText("/Volumes/Media"), {
      target: { value: "/Volumes/Anime" },
    });
    expect(save).not.toHaveBeenCalled();
    expect(readMappingDraft("peer-a")?.mappings).toEqual([
      { sourceRootId: "child", localPath: "/Volumes/Anime" },
    ]);
    fireEvent.click(screen.getByText("federation.save"));
    await waitFor(() =>
      expect(save).toHaveBeenCalledWith(
        [{ sourceRootId: "child", localPath: "/Volumes/Anime" }],
        [],
      ),
    );
    await waitFor(() => expect(readMappingDraft("peer-a")).toBeUndefined());
  });

  it("pages at most 200 nodes per request and does not recursively expand a large tree", async () => {
    vi.mocked(federationPeerApi.mappingTree).mockResolvedValueOnce({
      nodes: Array.from({ length: 200 }, (_, index) =>
        node(`root-${index}`, `Folder ${index}`, true),
      ),
      nextCursor: "next",
    });
    show();
    await open();
    expect(await screen.findAllByRole("treeitem")).toHaveLength(200);
    expect(federationPeerApi.mappingTree).toHaveBeenCalledTimes(1);
    vi.mocked(federationPeerApi.mappingTree).mockResolvedValueOnce({
      nodes: [node("root-200", "Folder 200")],
    });
    fireEvent.click(screen.getByText("federation.mappings.tree.more"));
    expect(await screen.findByText("Folder 200")).toBeInTheDocument();
    expect(federationPeerApi.mappingTree).toHaveBeenLastCalledWith(
      "peer-a",
      { parent: undefined, cursor: "next" },
      expect.any(AbortSignal),
    );
    expect(screen.getAllByRole("treeitem")).toHaveLength(201);
  });

  it("falls back only for explicit unsupported endpoints, preserving legacy root selection", async () => {
    vi.mocked(federationPeerApi.mappingTree).mockRejectedValueOnce(
      new FederationError("EndpointNotFound", "", 404),
    );
    show();
    await open();
    expect(await screen.findByText("Old library")).toBeInTheDocument();
    expect(federationPeerApi.mappingRoots).toHaveBeenCalledWith("peer-a", expect.any(AbortSignal));
  });

  it.each([401, 403, 503])(
    "does not conceal HTTP %s as an older peer, and refresh does not discard edits",
    async (status) => {
      vi.mocked(federationPeerApi.mappingTree).mockRejectedValueOnce(
        new FederationError(`Http${status}`, "", status),
      );
      show({ ...peer(), pathMappings: [{ sourceRootId: "saved", localPath: "/old" }] });
      fireEvent.change(screen.getByDisplayValue("/old"), { target: { value: "/draft" } });
      await open();
      expect(await screen.findByText(`Http${status}`)).toBeInTheDocument();
      expect(federationPeerApi.mappingRoots).not.toHaveBeenCalled();
      fireEvent.click(screen.getByText("federation.mappings.tree.refresh"));
      expect(await screen.findByText("Shared", { selector: "p" })).toBeInTheDocument();
      expect(screen.getByDisplayValue("/draft")).toBeInTheDocument();
    },
  );

  it("aborts an old peer request and rejects its late data while preserving per-target drafts", async () => {
    let done!: (page: any) => void;

    vi.mocked(federationPeerApi.mappingTree).mockReturnValueOnce(
      new Promise((resolve) => {
        done = resolve;
      }),
    );
    const { view, save } = show({
      ...peer(),
      pathMappings: [{ sourceRootId: "a", localPath: "/a" }],
    });

    fireEvent.change(screen.getByDisplayValue("/a"), { target: { value: "/a-draft" } });
    await open();
    const signal = vi.mocked(federationPeerApi.mappingTree).mock.calls[0]![2]!;

    view.rerender(<PeerPathMappings busy={false} peer={peer("peer-b")} onSave={save} />);
    expect(signal.aborted).toBe(true);
    await act(async () => done({ nodes: [node("wrong", "Wrong peer folder")] }));
    expect(screen.queryByText("Wrong peer folder")).toBeNull();
    view.rerender(
      <PeerPathMappings
        busy={false}
        peer={{ ...peer(), pathMappings: [{ sourceRootId: "a", localPath: "/a" }] }}
        onSave={save}
      />,
    );
    expect(screen.getByDisplayValue("/a")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.mappings.draft.restore"));
    expect(screen.getByDisplayValue("/a-draft")).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
  });

  it("restores explicitly, warns on sharing changes, and preserves a draft after failed saves", async () => {
    writeMappingDraft(peer(), [{ sourceRootId: "saved", localPath: "/draft" }]);
    const save = vi.fn().mockRejectedValue(new Error("Offline"));

    show(
      {
        ...peer(),
        outboundGrant: { grantId: "changed", revision: 2 },
        pathMappings: [{ sourceRootId: "saved", localPath: "/current" }],
      },
      save,
    );
    expect(screen.getByText("federation.mappings.draft.changed")).toBeInTheDocument();
    expect(screen.getByDisplayValue("/current")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.mappings.draft.restore"));
    fireEvent.click(screen.getByText("federation.save"));
    expect(save).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.mappings.replace"));
    expect(await screen.findByText("Offline")).toBeInTheDocument();
    expect(screen.getByDisplayValue("/draft")).toBeInTheDocument();
    expect(readMappingDraft("peer-a")?.mappings[0]?.localPath).toBe("/draft");
    fireEvent.click(screen.getByText("federation.mappings.draft.discard"));
    expect(screen.getByDisplayValue("/current")).toBeInTheDocument();
    expect(localStorage.getItem(mappingDraftKey("peer-a"))).toBeNull();
  });

  it("previews the most specific target rule with that path's separator rather than the browser OS", () => {
    const chain = [node("root", "Shared"), node("child", "Anime"), node("leaf", "Season")];

    expect(mappedDirectoryPreview(chain, [{ sourceRootId: "root", localPath: "Z:\\Media" }])).toBe(
      "Z:\\Media\\Anime\\Season",
    );
    expect(
      mappedDirectoryPreview(chain, [
        { sourceRootId: "root", localPath: "/Volumes/Media" },
        { sourceRootId: "child", localPath: "/Volumes/Anime" },
      ]),
    ).toBe("/Volumes/Anime/Season");
    clearMappingDraft("peer-a");
  });

  it("isolates drafts by target and API endpoint, stores no peer credentials, and tolerates browser storage failure", async () => {
    const one = peer();

    writeMappingDraft(one, [{ sourceRootId: "folder", localPath: "/draft" }]);
    expect(mappingDraftKey("peer-a")).not.toBe(mappingDraftKey("peer-b"));
    expect(mappingDraftKey("peer-a")).toContain(encodeURIComponent(window.location.origin));
    expect(readMappingDraft("peer-b")).toBeUndefined();
    expect(
      Object.keys(JSON.parse(localStorage.getItem(mappingDraftKey("peer-a"))!)).sort(),
    ).toEqual(["grantId", "grantRevision", "mappings", "version"]);
    clearMappingDraft("peer-a");
    show({ ...one, pathMappings: [{ sourceRootId: "folder", localPath: "/old" }] });
    const failed = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("quota");
    });

    fireEvent.change(screen.getByDisplayValue("/old"), { target: { value: "/in-memory-draft" } });
    expect(await screen.findByText("federation.mappings.draft.unavailable")).toBeInTheDocument();
    expect(screen.getByDisplayValue("/in-memory-draft")).toBeInTheDocument();
    failed.mockRestore();
  });
});
