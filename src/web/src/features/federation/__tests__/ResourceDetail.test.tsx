import type { FederatedResourceDetail, ResourceRef } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceDetail from "../components/ResourceDetail";
import { federationResourceApi } from "../resourceApi";

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: vi.fn(),
  useIsPureClient: () => false,
}));
vi.mock("../resourceApi", () => ({
  federationResourceApi: { detail: vi.fn(), playback: vi.fn(), openDirectory: vi.fn() },
  localMediaUrl: (url: string) => url,
}));

const remote: ResourceRef = { nodeId: "other", libraryEpoch: "other-epoch", resourceId: 1 };
const detail = (ref = remote): FederatedResourceDetail => ({
  ref,
  ownerLabel: "Other computer",
  displayName: "Remote title",
  availability: "HasFile",
  directoryAccess: { canOpen: false, reason: "PathMappingRequired" },
  properties: [{ label: "Remote category", type: "text", value: "Remote-only value", scope: 1 }],
  sources: [],
  externalIdentities: [],
  collections: [{ name: "Remote collection" }],
  assets: [
    {
      assetId: "opaque",
      kind: "video",
      fileName: "movie.mp4",
      contentType: "video/mp4",
      expiresAt: "2030-01-01T00:00:00Z",
    },
  ],
});
const view = (ref = remote) => (
  <MemoryRouter>
    <ResourceDetail
      localEpoch="local-epoch"
      localNodeId="local"
      resourceRef={ref}
      onClose={() => {}}
    />
  </MemoryRouter>
);

beforeEach(() => vi.clearAllMocks());
afterEach(cleanup);

describe("read-only remote detail", () => {
  it("renders self-contained metadata and never starts media or offers local management for a colliding ID", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    render(view());
    expect(await screen.findByText("Remote-only value")).toBeInTheDocument();
    expect(screen.getByText("Remote collection")).toBeInTheDocument();
    expect(screen.queryByText("federation.manageLocal")).not.toBeInTheDocument();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
    expect(federationResourceApi.detail).toHaveBeenCalledWith(remote, expect.any(AbortSignal));
    expect(document.querySelector("video")).toBeNull();
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      url: "/federation/local/media/session",
      launched: false,
      contentType: "video/mp4",
      expiresAt: "2030-01-01T00:00:00Z",
    });
    fireEvent.click(screen.getByText("federation.preview"));
    await waitFor(() =>
      expect(document.querySelector("video")).toHaveAttribute(
        "src",
        "/federation/local/media/session",
      ),
    );
    expect(federationResourceApi.playback).toHaveBeenCalledWith(
      remote,
      "opaque",
      "preview",
      expect.any(AbortSignal),
    );
  });
  it("does not turn an epoch mismatch into a local-management link", async () => {
    const stale = { ...remote, nodeId: "local" };

    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail(stale)] });
    render(view(stale));
    await screen.findByText("Remote title");
    expect(screen.queryByText("federation.manageLocal")).not.toBeInTheDocument();
  });
  it("rejects a detail response for the wrong owner instead of borrowing its numeric ID", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [detail({ ...remote, nodeId: "local" })],
    });
    render(view());
    expect(await screen.findByText("ResourceGone")).toBeInTheDocument();
    expect(screen.queryByText("Remote title")).not.toBeInTheDocument();
  });
  it("shows player failure and only reports launch after backend confirmation", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      launched: false,
      contentType: "video/mp4",
      expiresAt: "2030-01-01T00:00:00Z",
    });
    render(view());
    fireEvent.click(await screen.findByText("federation.playHere"));
    expect(await screen.findByText("PlayerUnavailable")).toBeInTheDocument();
    expect(screen.queryByText("federation.playerLaunched")).not.toBeInTheDocument();
  });
});

describe("opening a folder on this device", () => {
  it.each([
    "PathMappingRequired",
    "MappedPathUnavailable",
    "NoLinkedFile",
    "OpenDirectoryUnavailable",
  ])("explains %s without sending an action", async (reason) => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [{ ...detail(), directoryAccess: { canOpen: false, reason } }],
    });
    render(view());
    expect(await screen.findByText(`federation.error.${reason}`)).toBeInTheDocument();
    expect(screen.getByText("federation.directory.open")).toBeDisabled();
    expect(federationResourceApi.openDirectory).not.toHaveBeenCalled();
  });
  it.each([remote, { nodeId: "local", libraryEpoch: "local-epoch", resourceId: 1 }])(
    "sends the full identity to the local action for $nodeId only after a click",
    async (ref) => {
      vi.mocked(federationResourceApi.detail).mockResolvedValue({
        resources: [{ ...detail(ref), directoryAccess: { canOpen: true } }],
      });
      vi.mocked(federationResourceApi.openDirectory).mockResolvedValue({ opened: true });
      render(view(ref));
      const button = await screen.findByText("federation.directory.open");

      expect(federationResourceApi.openDirectory).not.toHaveBeenCalled();
      fireEvent.click(button);
      expect(await screen.findByText("federation.directory.opened")).toBeInTheDocument();
      expect(federationResourceApi.openDirectory).toHaveBeenCalledWith(
        ref,
        expect.any(AbortSignal),
      );
      expect(federationResourceApi.playback).not.toHaveBeenCalled();
    },
  );
  it("does not report an opened directory unless the backend confirms it", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [{ ...detail(), directoryAccess: { canOpen: true } }],
    });
    vi.mocked(federationResourceApi.openDirectory).mockResolvedValue({ opened: false });
    render(view());
    fireEvent.click(await screen.findByText("federation.directory.open"));
    expect(await screen.findByRole("alert")).toHaveTextContent("OpenDirectoryUnavailable");
    expect(screen.queryByText("federation.directory.opened")).not.toBeInTheDocument();
  });
  it("aborts an in-flight open action on a resource change and ignores its late result", async () => {
    let complete!: (value: { opened: boolean }) => void;

    vi.mocked(federationResourceApi.openDirectory).mockImplementation(
      () =>
        new Promise((resolve) => {
          complete = resolve;
        }),
    );
    vi.mocked(federationResourceApi.detail).mockImplementation(async (ref) => ({
      resources: [{ ...detail(ref), directoryAccess: { canOpen: true } }],
    }));
    const rendered = render(view());

    fireEvent.click(await screen.findByText("federation.directory.open"));
    const signal = vi.mocked(federationResourceApi.openDirectory).mock.calls[0][1];

    rendered.rerender(view({ ...remote, resourceId: 2 }));
    expect(signal?.aborted).toBe(true);
    await act(async () => complete({ opened: true }));
    expect(screen.queryByText("federation.directory.opened")).not.toBeInTheDocument();
  });
});
