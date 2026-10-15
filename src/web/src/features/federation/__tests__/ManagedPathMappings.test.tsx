import type { ManagedServer } from "../types";
import type { ManagedMappingTreeNode } from "../components/managedMappingTypes";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ManagedServerPathMappings from "../components/ManagedServerPathMappings";
import { mappedDirectoryPreview } from "../components/ManagedMappingTree";
import {
  mappingDraftKey,
  readMappingDraft,
  writeMappingDraft,
} from "../components/managedMappingDrafts";
import { managedServerApi } from "../serverApi";

import { ManagedServerState } from "@/sdk/constants";
vi.mock("../serverApi", () => ({ managedServerApi: { mappingTree: vi.fn() } }));
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
const server = (serverId = "server-a"): ManagedServer => ({
  serverId,
  address: "http://localhost:5555",
  pairedAt: "2026-10-15",
  state: ManagedServerState.Online,
  pathMappings: [],
});
const node = (serverPath: string, hasChildren = false): ManagedMappingTreeNode => ({
  serverPath,
  name: serverPath.split("/").pop() || "/",
  hasChildren,
  resourceCount: 10,
});
const saved = () => ({
  ...server(),
  pathMappings: [{ serverPath: "/Media", localPath: "/saved" }],
});
const show = (target = server(), save = vi.fn().mockResolvedValue(true)) => ({
  save,
  view: render(<ManagedServerPathMappings busy={false} server={target} onSave={save} />),
});
const open = async () => {
  const details = screen.getByText("federation.servers.mappings.title").closest("details")!;

  details.open = true;
  fireEvent(details, new Event("toggle"));
  await waitFor(() => expect(managedServerApi.mappingTree).toHaveBeenCalled());
};

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
  vi.mocked(managedServerApi.mappingTree).mockResolvedValue({
    libraryEpoch: "epoch-a",
    nodes: [node("/Volumes", true)],
  });
});
afterEach(cleanup);
describe("managed mapping tree and drafts", () => {
  it("retains existing mappings and lazily loads epoch-bound children without saving selection", async () => {
    const { save } = show(saved());

    expect(screen.getByDisplayValue("/saved")).toBeInTheDocument();
    expect(managedServerApi.mappingTree).not.toHaveBeenCalled();
    await open();
    expect(await screen.findByText("Volumes")).toBeInTheDocument();
    vi.mocked(managedServerApi.mappingTree).mockResolvedValueOnce({
      libraryEpoch: "epoch-a",
      nodes: [node("/Volumes/Media")],
    });
    fireEvent.click(screen.getByLabelText("federation.managedMappings.tree.expand"));
    expect(await screen.findByText("Media")).toBeInTheDocument();
    expect(managedServerApi.mappingTree).toHaveBeenLastCalledWith(
      "server-a",
      { parent: "/Volumes", cursor: undefined, libraryEpoch: "epoch-a" },
      expect.any(AbortSignal),
    );
    fireEvent.click(screen.getAllByLabelText("federation.managedMappings.tree.select")[1]!);
    expect(screen.getByDisplayValue("/Volumes/Media")).toBeInTheDocument();
    expect(save).not.toHaveBeenCalled();
    expect(readMappingDraft("server-a")?.mappings).toEqual([
      ...saved().pathMappings,
      { serverPath: "/Volumes/Media", localPath: "" },
    ]);
  });
  it("pages only the expanded level rather than recursively loading a large tree", async () => {
    vi.mocked(managedServerApi.mappingTree).mockResolvedValueOnce({
      libraryEpoch: "epoch-a",
      nodes: Array.from({ length: 200 }, (_, i) => node(`/folder${i}`, true)),
      nextCursor: "next",
    });
    show();
    await open();
    expect(await screen.findAllByRole("treeitem")).toHaveLength(200);
    expect(managedServerApi.mappingTree).toHaveBeenCalledTimes(1);
    vi.mocked(managedServerApi.mappingTree).mockResolvedValueOnce({
      libraryEpoch: "epoch-a",
      nodes: [node("/last")],
    });
    fireEvent.click(screen.getByText("federation.managedMappings.tree.more"));
    expect(await screen.findByText("last")).toBeInTheDocument();
    expect(managedServerApi.mappingTree).toHaveBeenLastCalledWith(
      "server-a",
      { parent: undefined, cursor: "next", libraryEpoch: "epoch-a" },
      expect.any(AbortSignal),
    );
  });
  it("restores explicitly and clears the draft only after a successful save", async () => {
    const target = saved();

    writeMappingDraft(target, [{ serverPath: "/Media", localPath: "/draft" }], target.pathMappings);
    const save = vi.fn().mockRejectedValueOnce(new Error("Offline")).mockResolvedValueOnce(true);

    show(target, save);
    expect(screen.getByDisplayValue("/saved")).toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.managedMappings.draft.restore"));
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    expect(await screen.findByText("Offline")).toBeInTheDocument();
    expect(readMappingDraft(target.serverId)?.mappings[0]?.localPath).toBe("/draft");
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    await waitFor(() => expect(readMappingDraft(target.serverId)).toBeUndefined());
    expect(save).toHaveBeenLastCalledWith(
      [{ serverPath: "/Media", localPath: "/draft" }],
      target.pathMappings,
    );
  });
  it("reviews concurrent changes and checks again when saved mappings change during review", async () => {
    const { save, view } = show(saved());

    fireEvent.change(screen.getByDisplayValue("/saved"), { target: { value: "/mine" } });
    const changed = {
      ...saved(),
      pathMappings: [{ serverPath: "/Media", localPath: "/elsewhere" }],
    };

    view.rerender(<ManagedServerPathMappings busy={false} server={changed} onSave={save} />);
    fireEvent.click(screen.getByText("federation.servers.mappings.save"));
    expect(save).not.toHaveBeenCalled();
    expect(screen.getByText("/Media → /elsewhere")).toBeInTheDocument();
    const latest = { ...changed, pathMappings: [{ serverPath: "/Media", localPath: "/latest" }] };

    view.rerender(<ManagedServerPathMappings busy={false} server={latest} onSave={save} />);
    fireEvent.click(screen.getByText("federation.managedMappings.replace"));
    expect(save).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.managedMappings.replace"));
    await waitFor(() =>
      expect(save).toHaveBeenCalledWith(
        [{ serverPath: "/Media", localPath: "/mine" }],
        latest.pathMappings,
      ),
    );
  });
  it("cancels old requests across devices and isolates drafts", async () => {
    let finish!: (result: any) => void;

    vi.mocked(managedServerApi.mappingTree).mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    const { view, save } = show(saved());

    fireEvent.change(screen.getByDisplayValue("/saved"), { target: { value: "/draft-a" } });
    await open();
    const signal = vi.mocked(managedServerApi.mappingTree).mock.calls[0]![2]!;

    view.rerender(
      <ManagedServerPathMappings busy={false} server={server("server-b")} onSave={save} />,
    );
    expect(signal.aborted).toBe(true);
    await act(async () => finish({ libraryEpoch: "epoch-a", nodes: [node("/stale")] }));
    expect(screen.queryByText("stale")).toBeNull();
    expect(readMappingDraft("server-b")).toBeUndefined();
    view.rerender(<ManagedServerPathMappings busy={false} server={saved()} onSave={save} />);
    fireEvent.click(screen.getByText("federation.managedMappings.draft.restore"));
    expect(screen.getByDisplayValue("/draft-a")).toBeInTheDocument();
  });
  it("refresh rejects late responses and retains unsaved edits", async () => {
    let finish!: (result: any) => void;

    vi.mocked(managedServerApi.mappingTree).mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    show(saved());
    fireEvent.change(screen.getByDisplayValue("/saved"), { target: { value: "/draft" } });
    await open();
    fireEvent.click(screen.getByText("federation.managedMappings.tree.refresh"));
    expect(await screen.findByText("Volumes")).toBeInTheDocument();
    await act(async () => finish({ libraryEpoch: "old", nodes: [node("/stale")] }));
    expect(screen.queryByText("stale")).toBeNull();
    expect(screen.getByDisplayValue("/draft")).toBeInTheDocument();
  });
  it("refuses to mix two library generations", async () => {
    show();
    await open();
    expect(await screen.findByText("Volumes")).toBeInTheDocument();
    vi.mocked(managedServerApi.mappingTree).mockResolvedValueOnce({
      libraryEpoch: "new",
      nodes: [node("/wrong")],
    });
    fireEvent.click(screen.getByLabelText("federation.managedMappings.tree.expand"));
    expect(await screen.findByText("LibraryEpochChanged")).toBeInTheDocument();
    expect(screen.queryByText("wrong")).toBeNull();
  });
  it("uses the host folder picker and retains edits when browser storage is unavailable", async () => {
    show(saved());
    fireEvent.click(screen.getByLabelText("federation.managedMappings.chooseLocal"));
    const options = portals.mock.lastCall![1];

    expect(options).toMatchObject({
      targetType: "folder",
      startPath: "/saved",
      defaultSelectedPath: "/saved",
    });
    const fail = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("quota");
    });

    await act(async () => options.onSelected({ path: "/chosen" }));
    expect(screen.getByDisplayValue("/chosen")).toBeInTheDocument();
    expect(
      await screen.findByText("federation.managedMappings.draft.unavailable"),
    ).toBeInTheDocument();
    fail.mockRestore();
  });
  it("stores only mapping edits, baseline and pairing timestamp per service and target", () => {
    writeMappingDraft(server(), [{ serverPath: "/Media", localPath: "Z:\\Media" }], []);
    expect(mappingDraftKey("server-a")).not.toBe(mappingDraftKey("server-b"));
    expect(mappingDraftKey("server-a")).toContain(encodeURIComponent(window.location.origin));
    expect(
      Object.keys(JSON.parse(localStorage.getItem(mappingDraftKey("server-a"))!)).sort(),
    ).toEqual(["baseMappings", "mappings", "pairedAt", "version"]);
  });
  it.each([
    [
      "/media/Anime/One",
      [{ serverPath: "/media", localPath: "Z:\\Media" }],
      "Z:\\Media\\Anime\\One",
    ],
    [
      "D:\\Media\\Anime",
      [{ serverPath: "d:/media", localPath: "/Volumes/Media" }],
      "/Volumes/Media/Anime",
    ],
    ["/media-backup/One", [{ serverPath: "/media", localPath: "/elsewhere" }], undefined],
    [
      "/media/Anime/One",
      [
        { serverPath: "/media", localPath: "/broad" },
        { serverPath: "/media/Anime", localPath: "/specific" },
      ],
      "/specific/One",
    ],
    ["/media/One", [{ serverPath: "/", localPath: "/" }], "/media/One"],
    ["/", [{ serverPath: "/", localPath: "/" }], "/"],
    ["/media", [{ serverPath: "/media", localPath: "Z:\\" }], "Z:\\"],
    ["/media/work", [{ serverPath: "/media", localPath: "Z:/" }], "Z:\\work"],
    ["//NAS/Share/Movie.mp4", [{ serverPath: "//nas/share", localPath: "Z:\\" }], "Z:\\Movie.mp4"],
    [
      "\\\\NAS\\Share\\Movie.mp4",
      [{ serverPath: "//nas/share", localPath: "//Other/Media" }],
      "//Other/Media\\Movie.mp4",
    ],
    ["//nas/share/movie.mp4", [{ serverPath: "/", localPath: "/mounted" }], undefined],
    ["Y:/movie.mp4", [{ serverPath: "/", localPath: "/mounted" }], undefined],
    ["relative/movie.mp4", [{ serverPath: "/", localPath: "/mounted" }], undefined],
    [
      "/media/movie.mp4",
      [
        { serverPath: "/", localPath: "/root" },
        { serverPath: "/media", localPath: "Z:\\" },
      ],
      "Z:\\movie.mp4",
    ],
    ["/media/a\\b", [{ serverPath: "/", localPath: "/root" }], "/root/media/a\\b"],
    ["/media", [{ serverPath: "/media", localPath: "   " }], undefined],
  ])(
    "previews %s using segment boundaries, longest prefix and target separators",
    (path, mappings, expected) => {
      expect(mappedDirectoryPreview(path as string, mappings as any)).toBe(expected);
    },
  );
});
