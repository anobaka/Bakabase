import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AddLinkWizard from "../components/AddLinkWizard";
import { dataSyncApi } from "../api";
import { useDataSyncActions } from "../hooks/useDataSyncActions";

import { candidate, link } from "./dataSyncFixtures";

import { DataSyncLinkMode, DataSyncLinkState, RemoteAccessMode } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) =>
      options
        ? [
            key,
            ...Object.entries(options)
              .filter(([name, value]) => name !== "defaultValue" && value !== undefined)
              .map(([, value]) => String(value)),
          ].join(" ")
        : key,
    i18n: { language: "en", changeLanguage: vi.fn(), exists: () => false },
  }),
  initReactI18next: { type: "3rdParty", init: vi.fn() },
}));
vi.mock("../api", async (importOriginal) => ({
  ...(await importOriginal<typeof Api>()),
  dataSyncApi: {
    peers: vi.fn(),
    createLink: vi.fn(),
    copyOnce: vi.fn(),
    setSharing: vi.fn(async () => undefined),
  },
}));

const found = [
  candidate("node-nas", "NAS", { linkId: 1, weMayRead: true, theyMayRead: true }),
  candidate("node-old", "Old NAS", { contractVersion: undefined }),
  candidate("node-lib", "Library PC", { sharesDefinitions: false }),
  candidate("node-new", "New PC"),
  candidate("node-read", "Reader PC", { weMayRead: true, theyMayRead: true }),
];

const open = (canManage = true, onClose = vi.fn()) => {
  // The page's own actions: the wizard shows what they say.
  const Host = () => {
    const hostActions = useDataSyncActions(() => undefined);

    return (
      <AddLinkWizard
        sharingEnabled
        actions={hostActions}
        canManage={canManage}
        remoteAccessMode={RemoteAccessMode.Enabled}
        selfName="This PC"
        onClose={onClose}
      />
    );
  };

  render(
    <MemoryRouter>
      <Host />
    </MemoryRouter>,
  );

  return onClose;
};

const option = (nodeId: string) =>
  document.querySelector<HTMLButtonElement>(`[data-candidate="${nodeId}"]`)!;

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(dataSyncApi.peers).mockImplementation(async (discover) =>
    discover ? found : found.slice(0, 1),
  );
});
afterEach(cleanup);

describe("sync with another device", () => {
  it("says what each device can do, and lets only those pick that can be linked", async () => {
    open();
    await waitFor(() => expect(option("node-new")).not.toBeNull());

    expect(option("node-nas")).toHaveAttribute("data-status", "linked");
    expect(option("node-old")).toHaveAttribute("data-status", "tooOld");
    expect(option("node-lib")).toHaveAttribute("data-status", "notSharing");
    expect(option("node-new")).toHaveAttribute("data-status", "asks");
    expect(option("node-read")).toHaveAttribute("data-status", "readable");
    expect(option("node-lib")).toHaveAttribute("aria-disabled", "true");
    fireEvent.click(option("node-lib"));
    expect(screen.getByTestId("data-sync-wizard-next")).toBeDisabled();
    expect(screen.getByText("dataSync.wizard.notListed")).toBeInTheDocument();
  });

  it("asks a device for access both ways, with the consent, and says where to approve", async () => {
    vi.mocked(dataSyncApi.createLink).mockResolvedValue({
      link: link(20, "node-new", "New PC", { state: DataSyncLinkState.AwaitingAccess }),
      requestId: "req-out-2",
    });
    const onClose = open();

    await waitFor(() => expect(option("node-new")).not.toBeNull());
    fireEvent.click(option("node-new"));
    fireEvent.click(screen.getByTestId("data-sync-wizard-next"));
    expect(screen.getByTestId("data-sync-wizard-how-twoWay")).toBeChecked();
    expect(screen.getByTestId("data-sync-wizard-consent")).toHaveTextContent(
      "dataSync.twoWay.consent New PC",
    );
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-wizard-start"));
    });
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "node-new",
      mode: DataSyncLinkMode.TwoWay,
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(screen.getByTestId("data-sync-wizard-outcome")).toHaveAttribute(
      "data-outcome",
      "requested",
    );
    expect(screen.getByText("dataSync.wizard.requested New PC")).toBeInTheDocument();
    fireEvent.click(screen.getByText("dataSync.done"));
    expect(onClose).toHaveBeenCalledWith("node-new");
  });

  it("copies once from a device it reads already, straight to the review", async () => {
    vi.mocked(dataSyncApi.copyOnce).mockResolvedValue({
      linkId: 21,
      reviewId: "review-2",
      copyOnce: true,
      linkMode: DataSyncLinkMode.Off,
    });
    open();
    await waitFor(() => expect(option("node-read")).not.toBeNull());
    fireEvent.click(option("node-read"));
    fireEvent.click(screen.getByTestId("data-sync-wizard-next"));
    fireEvent.click(screen.getByTestId("data-sync-wizard-how-copyOnce"));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-wizard-start"));
    });
    expect(dataSyncApi.copyOnce).toHaveBeenCalledWith({
      peerNodeId: "node-read",
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(screen.getByText("dataSync.link.review").closest("a")).toHaveAttribute(
      "href",
      "/data-sync?link=21&review=1",
    );
  });

  it("reaches a device by address and code where this window may create access", async () => {
    vi.mocked(dataSyncApi.createLink).mockResolvedValue({
      link: link(22, "node-far", "Far PC", { state: DataSyncLinkState.AwaitingReview }),
    });
    open();
    fireEvent.click(screen.getByTestId("data-sync-wizard-by-address"));
    fireEvent.change(screen.getByLabelText("dataSync.wizard.address"), {
      target: { value: "10.0.0.5:34567" },
    });
    fireEvent.change(screen.getByLabelText("dataSync.wizard.code"), {
      target: { value: "1234abcd5678" },
    });
    // Digits only, eight of them.
    expect(screen.getByLabelText("dataSync.wizard.code")).toHaveValue("12345678");
    fireEvent.click(screen.getByTestId("data-sync-wizard-next"));
    fireEvent.click(screen.getByTestId("data-sync-wizard-how-follow"));
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-wizard-start"));
    });
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      address: "10.0.0.5:34567",
      code: "12345678",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(screen.getByTestId("data-sync-wizard-outcome")).toHaveAttribute("data-outcome", "ready");
  });

  it("offers neither asking nor an address and code where this window may not create access", async () => {
    open(false);
    await waitFor(() => expect(option("node-new")).not.toBeNull());

    expect(option("node-new")).toHaveAttribute("data-status", "manageElsewhere");
    expect(option("node-new")).toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByTestId("data-sync-wizard-by-address")).toBeNull();
    // A device it reads already, and that reads it back, can still be linked both ways.
    fireEvent.click(option("node-read"));
    fireEvent.click(screen.getByTestId("data-sync-wizard-next"));
    expect(screen.getByTestId("data-sync-wizard-how-twoWay")).not.toBeDisabled();
  });
});
