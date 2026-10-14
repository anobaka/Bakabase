import type { FederatedResourceDetail, ResourceRef } from "../types";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import ResourceDetail from "../components/ResourceDetail";
import { federationResourceApi } from "../resourceApi";
import { FederationError } from "../transport";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore, useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { copyTextToClipboard } from "@/core/clipboard";

const openingFixture = vi.hoisted(() => ({
  choice: undefined as { mode: string; playerId?: string } | undefined,
  desktop: false,
  ready: true,
  unavailable: false,
  reload: vi.fn(),
  target: vi.fn(),
}));

vi.mock("@/features/playback/overrides", () => ({
  useTargetPlayback: (target: string, resourceId: number) => {
    openingFixture.target(target, resourceId);

    return {
      ready: openingFixture.ready,
      unavailable: openingFixture.unavailable,
      reload: openingFixture.reload,
      desktop: openingFixture.desktop,
      choice: () => openingFixture.choice,
    };
  },
  PlaybackOverridesButton: () => <button>opening-rules</button>,
}));
vi.mock("@/components/bakaui", () => ({
  Modal: ({ children }: any) => <div role="dialog">{children}</div>,
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
}));

vi.mock("@/core/clipboard", () => ({ copyTextToClipboard: vi.fn() }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));

vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: vi.fn(),
  useIsPureClient: () => false,
  useUserSideActionsRunHere: vi.fn(() => true),
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

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(useUserSideActionsRunHere).mockReturnValue(true);
  vi.mocked(useRemoteAccessStore).mockImplementation((selector: any) =>
    selector({
      context: "known",
      clientMode: vi.mocked(useUserSideActionsRunHere)()
        ? ClientMode.AllInOne
        : ClientMode.RemoteBrowser,
    }),
  );
  openingFixture.choice = undefined;
  openingFixture.desktop = false;
  openingFixture.ready = true;
  openingFixture.unavailable = false;
  openingFixture.reload.mockResolvedValue(undefined);
});
afterEach(cleanup);

describe("read-only remote detail", () => {
  it("offers a read-only desktop guide for shared media in a browser, without triggering playback", async () => {
    vi.mocked(useUserSideActionsRunHere).mockReturnValue(false);
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    render(view());
    fireEvent.click(await screen.findByText("resource.desktopPlayback.action"));
    expect(screen.getByText("resource.desktopPlayback.connectShared")).toBeInTheDocument();
    expect(screen.getByText("resource.desktopPlayback.sharedLimits")).toBeInTheDocument();
    expect(screen.queryByText("resource.desktopPlayback.connect")).not.toBeInTheDocument();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
  });
  it("does not offer desktop installation for a shared resource with no exposed assets", async () => {
    vi.mocked(useUserSideActionsRunHere).mockReturnValue(false);
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [{ ...detail(), assets: [] }],
    });
    render(view());
    await screen.findByText("Remote title");
    expect(screen.queryByText("resource.desktopPlayback.action")).not.toBeInTheDocument();
  });
  it("asks when shared rules cannot load, with retry and explicit preview still available", async () => {
    openingFixture.ready = false;
    openingFixture.unavailable = true;
    openingFixture.desktop = true;
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      url: "/media/test",
      launched: false,
      expiresAt: "2030-01-01",
      contentType: "video/mp4",
    });
    render(view());
    fireEvent.click(await screen.findByText("resource.openingRules.useDefault"));
    expect(screen.getByText("resource.playbackPolicy.rulesUnavailable")).toBeInTheDocument();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("resource.playbackPolicy.retryRules"));
    expect(openingFixture.reload).toHaveBeenCalledOnce();
    fireEvent.click(screen.getByText("resource.openingRules.useDefault"));
    fireEvent.click(screen.getByText("resource.openingRules.browser"));
    await waitFor(() =>
      expect(federationResourceApi.playback).toHaveBeenCalledWith(
        remote,
        "opaque",
        "preview",
        expect.any(AbortSignal),
      ),
    );
  });
  it.each([
    { desktop: false, mode: "preview" },
    { desktop: true, mode: "player" },
  ])(
    "uses the viewing surface's default for shared video (desktop=$desktop)",
    async ({ desktop, mode }) => {
      openingFixture.desktop = desktop;
      vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
      vi.mocked(federationResourceApi.playback).mockResolvedValue({
        url: "/media/test",
        launched: true,
        expiresAt: "2030-01-01",
        contentType: "video/mp4",
      });
      render(view());
      fireEvent.click(await screen.findByText("resource.openingRules.useDefault"));
      await waitFor(() =>
        expect(federationResourceApi.playback).toHaveBeenCalledWith(
          remote,
          "opaque",
          mode,
          expect.any(AbortSignal),
        ),
      );
      expect(openingFixture.target).toHaveBeenCalledWith("node:other:other-epoch", 1);
    },
  );
  it("applies a saved browser override without removing the explicit native alternative", async () => {
    openingFixture.desktop = true;
    openingFixture.choice = { mode: "browser" };
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      url: "/media/test",
      launched: true,
      expiresAt: "2030-01-01",
      contentType: "video/mp4",
    });
    render(view());
    fireEvent.click(await screen.findByText("resource.openingRules.useDefault"));
    await waitFor(() =>
      expect(federationResourceApi.playback).toHaveBeenLastCalledWith(
        remote,
        "opaque",
        "preview",
        expect.any(AbortSignal),
      ),
    );
    fireEvent.click(screen.getByText("federation.playHere"));
    await waitFor(() =>
      expect(federationResourceApi.playback).toHaveBeenLastCalledWith(
        remote,
        "opaque",
        "player",
        expect.any(AbortSignal),
      ),
    );
  });
  it("asks before launching when the target-specific method is ask", async () => {
    openingFixture.choice = { mode: "ask" };
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [detail()] });
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      url: "/media/test",
      launched: false,
      expiresAt: "2030-01-01",
      contentType: "video/mp4",
    });
    render(view());
    fireEvent.click(await screen.findByText("resource.openingRules.useDefault"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("resource.openingRules.browser"));
    await waitFor(() =>
      expect(federationResourceApi.playback).toHaveBeenCalledWith(
        remote,
        "opaque",
        "preview",
        expect.any(AbortSignal),
      ),
    );
  });
  it("explains an unavailable native preference without sending an unmapped image to the player", async () => {
    openingFixture.desktop = true;
    openingFixture.choice = { mode: "native", playerId: "system-default" };
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [
        { ...detail(), assets: [{ ...detail().assets[0], kind: "image", canOpenLocally: false }] },
      ],
    });
    render(view());
    fireEvent.click(await screen.findByText("resource.openingRules.useDefault"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText("resource.playbackPolicy.preferredUnavailable")).toBeInTheDocument();
    expect(screen.getByText("resource.openingRules.native")).toBeDisabled();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
  });
  it("offers only validated original webpages, without treating protocol or file URLs as websites", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [
        {
          ...detail(),
          sources: [
            { kind: "DLsite", url: "https://www.dlsite.com/work/123" },
            { kind: "Steam", url: "steam://rungameid/123" },
            { kind: "FileSystem", url: "file:///private/owner/path" },
            { kind: "Manual", url: "javascript:alert(1)" },
          ],
        },
      ],
    });
    render(view());
    expect(
      await screen.findByRole("link", { name: "federation.detail.originalPage" }),
    ).toHaveAttribute("href", "https://www.dlsite.com/work/123");
    expect(screen.getAllByRole("link")).toHaveLength(1);
    expect(screen.queryByText("file:///private/owner/path")).not.toBeInTheDocument();
  });

  it("copies a share-relative location with the owner name without requesting the owner's filesystem path", async () => {
    vi.mocked(useUserSideActionsRunHere).mockReturnValue(false);
    vi.mocked(copyTextToClipboard).mockResolvedValue(undefined);
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [
        { ...detail(), assets: [{ ...detail().assets[0]!, relativePath: "Films/movie.mp4" }] },
      ],
    });
    render(view());
    fireEvent.click(await screen.findByText("federation.detail.copySharedLocation"));
    expect(await screen.findByText("federation.detail.locationCopied")).toBeInTheDocument();
    expect(copyTextToClipboard).toHaveBeenCalledWith("Other computer: Films/movie.mp4");
    expect(federationResourceApi.openDirectory).not.toHaveBeenCalled();
    expect(screen.getByText("federation.directory.browserTip")).toBeInTheDocument();
  });

  it.each([undefined, false])(
    "keeps images preview-only without confirmed local availability (%s)",
    async (canOpenLocally) => {
      const image = {
        ...detail(),
        assets: [
          {
            ...detail().assets[0]!,
            kind: "image",
            fileName: "picture.jpg",
            contentType: "image/jpeg",
            canOpenLocally,
          },
        ],
      };

      vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [image] });
      render(view());
      await screen.findByText("picture.jpg");
      expect(screen.getByText("federation.preview")).toBeInTheDocument();
      expect(screen.queryByText("federation.openImageHere")).toBeNull();
      expect(federationResourceApi.playback).not.toHaveBeenCalled();
    },
  );

  it("opens a confirmed mapped image in the default app only after clicking and reports backend confirmation", async () => {
    const image = {
      ...detail(),
      assets: [
        {
          ...detail().assets[0]!,
          kind: "image",
          fileName: "picture.jpg",
          contentType: "image/jpeg",
          canOpenLocally: true,
        },
      ],
    };

    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [image] });
    vi.mocked(federationResourceApi.playback).mockResolvedValue({
      launched: true,
      contentType: "image/jpeg",
      expiresAt: "2030-01-01T00:00:00Z",
    });
    render(view());
    fireEvent.click(await screen.findByText("federation.openImageHere"));
    expect(await screen.findByText("federation.imageOpenedHere")).toBeInTheDocument();
    expect(federationResourceApi.playback).toHaveBeenCalledWith(
      remote,
      "opaque",
      "player",
      expect.any(AbortSignal),
    );
    expect(screen.queryByText("federation.playerLaunched")).toBeNull();
  });

  it("still hides image native actions in a server browser even if the DTO says the file is local", async () => {
    vi.mocked(useUserSideActionsRunHere).mockReturnValue(false);
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [
        { ...detail(), assets: [{ ...detail().assets[0]!, kind: "image", canOpenLocally: true }] },
      ],
    });
    render(view());
    await screen.findByText("Remote title");
    expect(screen.queryByText("federation.openImageHere")).toBeNull();
    expect(screen.getByText("federation.preview")).toBeInTheDocument();
  });
  it("offers browser previews without native players or file managers on a headless server", async () => {
    vi.mocked(useUserSideActionsRunHere).mockReturnValue(false);
    vi.mocked(federationResourceApi.detail).mockResolvedValue({
      resources: [{ ...detail(), directoryAccess: { canOpen: true } }],
    });
    render(view());
    await screen.findByText("Remote title");
    expect(screen.getByText("federation.preview")).toBeInTheDocument();
    expect(screen.queryByText("federation.playHere")).not.toBeInTheDocument();
    expect(screen.queryByText("federation.directory.open")).not.toBeInTheDocument();
    expect(federationResourceApi.playback).not.toHaveBeenCalled();
    expect(federationResourceApi.openDirectory).not.toHaveBeenCalled();
  });

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

describe("expired media leases", () => {
  const leased = (): FederatedResourceDetail => ({
    ...detail(),
    assets: [{ ...detail().assets[0], sourceRootId: "root", relativePath: "a/movie.mp4" }],
  });
  const session = {
    url: "/federation/local/media/renewed",
    launched: false,
    contentType: "video/mp4",
    expiresAt: "2030-01-01T00:00:00Z",
  };
  const expired = () => new FederationError("AssetExpired", "Lease expired", 410);

  it("re-reads the detail and plays the same file under its renewed ID", async () => {
    const stale = leased();
    const asset = stale.assets[0];

    vi.mocked(federationResourceApi.detail)
      .mockResolvedValueOnce({ resources: [stale] })
      .mockResolvedValueOnce({
        resources: [
          {
            ...stale,
            assets: [
              { ...asset, assetId: "same-name-elsewhere", relativePath: "b/movie.mp4" },
              { ...asset, assetId: "renewed" },
            ],
          },
        ],
      });
    vi.mocked(federationResourceApi.playback)
      .mockRejectedValueOnce(expired())
      .mockResolvedValueOnce(session);
    render(view());
    fireEvent.click(await screen.findByText("federation.preview"));
    await waitFor(() =>
      expect(document.querySelector("video")).toHaveAttribute("src", session.url),
    );
    expect(federationResourceApi.playback).toHaveBeenNthCalledWith(
      1,
      remote,
      "opaque",
      "preview",
      expect.any(AbortSignal),
    );
    expect(federationResourceApi.playback).toHaveBeenNthCalledWith(
      2,
      remote,
      "renewed",
      "preview",
      expect.any(AbortSignal),
    );
    expect(federationResourceApi.detail).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
  it("surfaces the expiry when the renewed detail has no single matching file", async () => {
    vi.mocked(federationResourceApi.detail)
      .mockResolvedValueOnce({ resources: [leased()] })
      .mockResolvedValueOnce({ resources: [{ ...leased(), assets: [] }] });
    vi.mocked(federationResourceApi.playback).mockRejectedValueOnce(expired());
    render(view());
    fireEvent.click(await screen.findByText("federation.preview"));
    expect(await screen.findByRole("alert")).toHaveTextContent("AssetExpired");
    expect(federationResourceApi.playback).toHaveBeenCalledOnce();
  });
  it("retries only once", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [leased()] });
    vi.mocked(federationResourceApi.playback).mockRejectedValue(expired());
    render(view());
    fireEvent.click(await screen.findByText("federation.playHere"));
    expect(await screen.findByRole("alert")).toHaveTextContent("AssetExpired");
    expect(federationResourceApi.playback).toHaveBeenCalledTimes(2);
    expect(federationResourceApi.detail).toHaveBeenCalledTimes(2);
  });
  it("does not renew for other playback failures", async () => {
    vi.mocked(federationResourceApi.detail).mockResolvedValue({ resources: [leased()] });
    vi.mocked(federationResourceApi.playback).mockRejectedValue(
      new FederationError("GrantRevoked", "Revoked", 403),
    );
    render(view());
    fireEvent.click(await screen.findByText("federation.preview"));
    expect(await screen.findByRole("alert")).toHaveTextContent("GrantRevoked");
    expect(federationResourceApi.detail).toHaveBeenCalledOnce();
  });
});
