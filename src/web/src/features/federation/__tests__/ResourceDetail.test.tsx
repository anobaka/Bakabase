import type { FederatedResourceDetail, ResourceRef } from "../types";

import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceDetail from "../components/ResourceDetail";
import { federationResourceApi } from "../resourceApi";

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: vi.fn(),
  useIsPureClient: () => false,
}));
vi.mock("../resourceApi", () => ({
  federationResourceApi: { detail: vi.fn(), playback: vi.fn() },
  localMediaUrl: (url: string) => url,
}));

const remote: ResourceRef = { nodeId: "other", libraryEpoch: "other-epoch", resourceId: 1 };
const detail = (ref = remote): FederatedResourceDetail => ({
  ref,
  ownerLabel: "Other computer",
  displayName: "Remote title",
  availability: "HasFile",
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
