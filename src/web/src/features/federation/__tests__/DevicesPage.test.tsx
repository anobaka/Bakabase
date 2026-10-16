import type { FederationStatus } from "../types";

import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { FederationError } from "../transport";
import DevicesPage from "../DevicesPage";
import { federationPeerApi } from "../peerApi";
import { managedServerApi } from "../serverApi";
import { useFederationStatus } from "../hooks/useFederationStatus";

import BApi from "@/sdk/BApi";
vi.mock("@/components/ContextProvider/BakabaseContextProvider", () => ({
  useBakabaseContext: () => ({ createPortal: vi.fn() }),
}));
vi.mock("@/components/FileSystemSelector", () => ({ FileSystemSelectorModal: () => null }));

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    // Keys as text, plus the requester address so tests can see where it is surfaced.
    t: (key: string, options?: { address?: string }) =>
      options?.address ? `${key} ${options.address}` : key,
    i18n: { language: "en", changeLanguage: vi.fn() },
  }),
}));
vi.mock("@/stores/remoteAccess", () => ({
  useRemoteAccessStore: (selector: (state: unknown) => unknown) =>
    selector({ initialized: true, isLocal: true }),
  useIsPureClient: () => false,
}));
vi.mock("../peerApi", () => ({
  federationPeerApi: { status: vi.fn(), setName: vi.fn(), resetIdentity: vi.fn() },
}));
vi.mock("../hooks/useFederationStatus", () => ({ useFederationStatus: vi.fn() }));
// The management sections have their own suites (ManagementSections.test.tsx). Here they
// sit idle: nothing managed, remote access off, so they never compete with the sharing
// controls these tests drive.
vi.mock("../serverApi", () => ({
  managedServerApi: {
    list: vi.fn().mockResolvedValue({ available: true, servers: [], requests: [] }),
  },
}));
vi.mock("@/sdk/BApi", () => ({
  default: {
    options: {},
    management: { getManagedDeviceName: vi.fn(), setManagedDeviceName: vi.fn() },
    remoteAccess: {
      getRemoteAccessSettings: vi.fn().mockResolvedValue({
        code: 0,
        data: {
          mode: 0,
          addresses: [],
          allowLiveTranscode: false,
          requirePairing: false,
          devices: [],
          pendingRequests: [],
        },
      }),
    },
  },
}));
const status: FederationStatus = {
  identity: { nodeId: "local", libraryEpoch: "epoch", name: "This PC" },
  remoteAccessMode: 0,
  requirePairing: false,
  reachableAddresses: [],
};

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(BApi.management.getManagedDeviceName).mockResolvedValue({
    name: "This PC",
    libraryEpoch: "epoch",
    environmentControlled: false,
  });
  vi.mocked(BApi.management.setManagedDeviceName).mockImplementation(async (input) => ({
    name: input.name ?? "Computer",
    libraryEpoch: "epoch",
    environmentControlled: false,
  }));
  localStorage.clear();
  vi.mocked(useFederationStatus).mockReturnValue({
    status,
    loading: false,
    error: undefined,
    refresh: vi.fn().mockResolvedValue(undefined),
  });
});
afterEach(cleanup);
/** Device identity and recovery use the same page in desktop and server installations. */
/** The add form, open even when devices are listed. */
const renderPage = (entry = "/federation/devices") =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <DevicesPage />
    </MemoryRouter>,
  );

describe("device permission workflows", () => {
  it("offers no identity action before the reader says what happened", () => {
    renderPage("/federation/devices?section=advanced");
    expect(screen.getByText("federation.identity.question")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.reset")).not.toBeInTheDocument();
    expect(screen.queryByText("federation.identity.restore")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.copied.title" }));
    expect(screen.getByText("federation.identity.copied.removes")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.copied.keeps")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.reset")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.restore")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    expect(screen.getByText("federation.identity.restored.after")).toBeInTheDocument();
    expect(screen.getByText("federation.identity.restore")).toBeInTheDocument();
    expect(screen.queryByText("federation.identity.reset")).not.toBeInTheDocument();
  });
  it("requires explicit confirmation before making a copied installation a new device", async () => {
    renderPage("/federation/devices?section=advanced");
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.copied.title" }));
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.identity.reset"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.identity.confirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    // Listed, then probed, as the page does when it opens.
    await waitFor(() => expect(managedServerApi.list).toHaveBeenCalledWith(true));
    const listed = vi.mocked(managedServerApi.list).mock.calls.length;

    fireEvent.click(screen.getByText("federation.confirm"));
    // A copy: the install's own identity goes too.
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true, true));
    // …and the servers it managed, which the page lists again rather than keep showing.
    await waitFor(() =>
      expect(vi.mocked(managedServerApi.list).mock.calls.slice(listed)).toContainEqual([false]),
    );
    await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
  });
  it("opens recovery help from configuration without resetting and restores with the original node identity", async () => {
    renderPage("/federation/devices?section=identity");
    const flow = screen.getByTestId("identity-recovery");

    expect(flow).toBeVisible();
    await waitFor(() => expect(flow).toHaveFocus());
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    fireEvent.click(screen.getByText("federation.identity.restore"));
    const dialog = screen.getByRole("alertdialog");

    expect(dialog).toHaveTextContent("federation.identity.restoreConfirm");
    // A backup brings back who could manage this device too, and this leaves them alone.
    expect(dialog).toHaveTextContent("federation.identity.restoreWarning");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false));
  });
  it("reports a failed recovery without claiming a reset or disabling sharing in a separate request", async () => {
    vi.mocked(federationPeerApi.resetIdentity).mockRejectedValueOnce(
      new Error("Storage unavailable"),
    );
    renderPage("/federation/devices?section=advanced");
    fireEvent.click(screen.getByRole("radio", { name: "federation.identity.restored.title" }));
    fireEvent.click(screen.getByText("federation.identity.restore"));
    fireEvent.click(screen.getByText("federation.confirm"));
    expect(await screen.findByText("Storage unavailable")).toBeInTheDocument();
    expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(false);
    expect(
      screen.getByRole("button", { name: "federation.identity.restore", hidden: true }),
    ).not.toBeDisabled();
  });
  it("shows the device ID in Advanced, with a way to copy it", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);

    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
    try {
      renderPage("/federation/devices?section=advanced");
      const block = within(screen.getByTestId("device-id"));

      expect(block.getByText("local")).toBeInTheDocument();
      // Named for what it copies — an ID, not an address — and says how it went.
      fireEvent.click(block.getByRole("button", { name: "federation.devices.id.copy" }));
      await waitFor(() => expect(writeText).toHaveBeenCalledWith("local"));
      expect(await block.findByRole("status")).toHaveTextContent("federation.copied");
    } finally {
      Reflect.deleteProperty(navigator, "clipboard");
    }
  });
});

describe("unreadable sharing state", () => {
  it("offers a confirmed reset as a new device even though no status could be loaded", async () => {
    const refresh = vi.fn().mockResolvedValue(undefined);

    vi.mocked(useFederationStatus).mockReturnValue({
      status: undefined,
      loading: false,
      error: new FederationError("SharingStateUnavailable", "Unreadable", 503),
      refresh,
    });
    renderPage();
    expect(screen.getByText("federation.recovery.title")).toBeInTheDocument();
    expect(screen.queryByText("federation.browsing.title")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("federation.recovery.reset"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent("federation.recovery.confirm");
    expect(federationPeerApi.resetIdentity).not.toHaveBeenCalled();
    fireEvent.click(screen.getByText("federation.confirm"));
    // Only the sharing state was lost: the install keeps its identity and paired devices.
    await waitFor(() => expect(federationPeerApi.resetIdentity).toHaveBeenCalledWith(true, false));
    await waitFor(() => expect(refresh).toHaveBeenCalled());
  });
  it("does not offer a reset for other load failures", () => {
    vi.mocked(useFederationStatus).mockReturnValue({
      status: undefined,
      loading: false,
      error: new FederationError("InternalError", "Failed", 500),
      refresh: vi.fn(),
    });
    renderPage();
    expect(screen.queryByText("federation.recovery.title")).not.toBeInTheDocument();
    expect(screen.queryByText("federation.recovery.reset")).not.toBeInTheDocument();
    expect(screen.getByTestId("federation-feedback")).toHaveTextContent("Failed");
  });
});

const withStatus = (patch: Partial<FederationStatus>) => {
  const refresh = vi.fn().mockResolvedValue(undefined);

  vi.mocked(useFederationStatus).mockReturnValue({
    status: { ...status, ...patch },
    loading: false,
    error: undefined,
    refresh,
  });

  return refresh;
};

describe("this device's address", () => {
  const writeText = vi.fn();
  /** What the server lists: every port on every interface, in its own order. */
  const listed = [
    { url: "http://198.18.0.1:34567", interfaceName: "utun4" },
    { url: "http://198.18.0.1:5000", interfaceName: "utun4" },
    { url: "http://192.168.1.5:34567", interfaceName: "en0" },
    { url: "http://192.168.1.5:5000", interfaceName: "en0" },
    { url: "http://100.101.1.2:34567", interfaceName: "utun7" },
    { url: "http://192.168.128.1:34567", interfaceName: "bridge100" },
    { url: "http://169.254.3.4:34567", interfaceName: "en5" },
  ];
  const withSettings = (mode: number, addresses = listed) =>
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockResolvedValue({
      code: 0,
      data: {
        mode,
        addresses,
        allowLiveTranscode: false,
        requirePairing: true,
        devices: [],
        pendingRequests: [],
      },
    } as never);

  beforeEach(() => {
    writeText.mockReset();
    Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  });
  afterEach(() => Reflect.deleteProperty(navigator, "clipboard"));

  it("recommends one address, preserves mapped ports, and folds away what cannot be reached", async () => {
    withSettings(1);
    renderPage("/federation/devices");
    const list = await screen.findByTestId("device-addresses");

    expect(list).toHaveAttribute("data-context", "device");
    const recommended = within(list).getByTestId("device-address-recommended");

    expect(recommended).toHaveTextContent("http://192.168.1.5:34567");
    expect(recommended).toHaveTextContent("federation.devices.addresses.recommended");
    // A VPN address is labelled, and other ports on the same host remain available.
    expect(within(list).getByText("http://100.101.1.2:34567")).toBeInTheDocument();
    expect(within(list).getByText(/federation\.devices\.addresses\.vpn/)).toBeInTheDocument();
    expect(within(list).getByText("http://192.168.1.5:5000")).toBeInTheDocument();
    // Adapters another device cannot reach wait behind a disclosure, labelled.
    expect(within(list).queryByText("http://198.18.0.1:34567")).not.toBeInTheDocument();
    const more = within(within(list).getByTestId("device-address-more")).getByRole("button", {
      name: "federation.devices.addresses.more",
    });

    expect(more).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(more);
    expect(within(list).getByText("http://198.18.0.1:34567")).toBeInTheDocument();
    expect(within(list).getByText("http://198.18.0.1:5000")).toBeInTheDocument();
    expect(within(list).getByText("http://192.168.128.1:34567")).toBeInTheDocument();
    expect(within(list).getByText("http://169.254.3.4:34567")).toBeInTheDocument();
    expect(within(list).getAllByText(/federation\.devices\.addresses\.virtual/)).toHaveLength(3);
    expect(within(list).getByText(/federation\.devices\.addresses\.linkLocal/)).toBeInTheDocument();
  });
  it("copies an address and says whether it could", async () => {
    writeText.mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("denied"));
    withSettings(1);
    renderPage("/federation/devices");
    const recommended = await screen.findByTestId("device-address-recommended");

    // Named by what it copies, beside the word it shows.
    const button = within(recommended).getByRole("button", {
      name: "federation.copy http://192.168.1.5:34567",
    });
    const list = screen.getByTestId("device-addresses");
    const announced = within(list).getByRole("status");

    expect(announced.textContent).toBe("");
    fireEvent.click(button);
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("http://192.168.1.5:34567"));
    expect(await within(recommended).findByText("federation.copied")).toBeInTheDocument();
    // …and says how it went where a screen reader hears it.
    expect(announced).toHaveTextContent("federation.copied");
    fireEvent.click(within(recommended).getByText("federation.copied"));
    expect(await within(recommended).findByText("federation.copyFailed")).toBeInTheDocument();
    expect(announced).toHaveTextContent("federation.copyFailed");
    // Named by its own title, once: by the place a link lands on, which holds the list.
    expect(list).not.toHaveAttribute("aria-label");
    expect(list).not.toHaveAttribute("role");
    expect(
      screen.getByRole("region", { name: "federation.devices.addresses.title" }),
    ).toContainElement(list);
  });
  it("names the place a link lands on while the addresses are still on their way", () => {
    vi.mocked(BApi.remoteAccess.getRemoteAccessSettings).mockReturnValueOnce(
      new Promise(() => {}) as never,
    );
    renderPage("/federation/devices");
    const place = screen.getByRole("region", { name: "federation.devices.addresses.title" });

    expect(place).toHaveAttribute("id", "device-addresses");
    expect(place).toHaveTextContent("federation.loading");
  });
  it("greys the addresses out while remote access is off, and says where to turn it on", async () => {
    withSettings(0);
    renderPage("/federation/devices");
    const list = await screen.findByTestId("device-addresses");

    expect(list).toHaveTextContent("federation.devices.addresses.remoteOff");
    const link = within(list).getByRole("link", {
      name: "federation.devices.openManagementAccess",
    });

    expect(link).toHaveAttribute("href", "/federation/devices?section=management");
    // Only the addresses are greyed out: the warning and its link keep their contrast.
    expect(within(list).getByTestId("device-address-recommended")).toHaveClass("opacity-60");
    for (let element: HTMLElement | null = link; element; element = element.parentElement)
      expect(element).not.toHaveClass("opacity-60");
  });
  it("points at the network when no address was found", async () => {
    withSettings(1, []);
    renderPage("/federation/devices");
    expect(await screen.findByText("federation.devices.addresses.none")).toBeInTheDocument();
  });
});

describe("device name", () => {
  const nameForm = () => within(screen.getByLabelText("federation.name.label").closest("form")!);

  it("renames this device and refreshes status", async () => {
    const refresh = withStatus({});

    renderPage("/federation/devices");
    fireEvent.click(screen.getByText("deviceAppearance.editMetadata"));
    fireEvent.change(await screen.findByLabelText("federation.name.label"), {
      target: { value: "  Study PC " },
    });
    fireEvent.click(nameForm().getByText("deviceAppearance.saveName"));
    await waitFor(() =>
      expect(BApi.management.setManagedDeviceName).toHaveBeenCalledWith(
        { name: "Study PC", expectedName: "This PC", libraryEpoch: "epoch" },
        { showErrorToast: false },
      ),
    );
    await waitFor(() => expect(refresh).toHaveBeenCalled());
    expect(screen.getByLabelText("federation.name.label")).toHaveValue("Study PC");
  });
  it("resets to the computer name", async () => {
    renderPage("/federation/devices");
    fireEvent.click(screen.getByText("deviceAppearance.editMetadata"));
    fireEvent.click(await screen.findByText("federation.name.reset"));
    await waitFor(() =>
      expect(BApi.management.setManagedDeviceName).toHaveBeenCalledWith(
        { name: undefined, expectedName: "This PC", libraryEpoch: "epoch" },
        { showErrorToast: false },
      ),
    );
  });
  it("keeps the edit open when the name is rejected", async () => {
    vi.mocked(BApi.management.setManagedDeviceName).mockRejectedValueOnce(
      new FederationError("InvalidDeviceName", "Bad name", 400),
    );
    renderPage("/federation/devices");
    fireEvent.click(screen.getByText("deviceAppearance.editMetadata"));
    await screen.findByLabelText("federation.name.label");
    fireEvent.click(nameForm().getByText("deviceAppearance.saveName"));
    expect(await screen.findByText("Bad name")).toBeInTheDocument();
    expect(screen.getByLabelText("federation.name.label")).toHaveValue("This PC");
  });
});
