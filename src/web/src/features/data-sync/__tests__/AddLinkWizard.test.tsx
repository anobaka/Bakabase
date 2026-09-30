import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import AddLinkWizard from "../components/AddLinkWizard";
import { dataSyncApi, DataSyncProblemError } from "../api";

import { blurWhenDisabled } from "./blurWhenDisabled";
import { candidate, link } from "./dataSyncFixtures";

import {
  DataSyncLinkMode,
  DataSyncLinkState,
  DataSyncProblemCode,
  RemoteAccessMode,
} from "@/sdk/constants";

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
    revokeReader: vi.fn(),
  },
}));
vi.mock("@/components/HelpCenter/HelpCenterButton", () => ({
  default: ({ section, topic }: { section: string; topic: string }) => (
    <span data-help={`${topic}/${section}`} data-testid="help" />
  ),
}));

const found = [
  candidate("node-nas", "NAS", { linkId: 1, weMayRead: true, theyMayRead: true }),
  candidate("node-old", "Old NAS", { contractVersion: undefined }),
  candidate("node-lib", "Library PC", { sharesDefinitions: false }),
  candidate("node-new", "New PC"),
  candidate("node-read", "Reader PC", { weMayRead: true, theyMayRead: true }),
];

const open = (
  canManage = true,
  own: { sharingEnabled?: boolean; remoteAccessMode?: RemoteAccessMode } = {},
) => {
  const onClose = vi.fn();

  render(
    <MemoryRouter>
      <AddLinkWizard
        canManage={canManage}
        remoteAccessMode={own.remoteAccessMode ?? RemoteAccessMode.Enabled}
        selfName="This PC"
        sharingEnabled={own.sharingEnabled ?? true}
        onClose={onClose}
      />
    </MemoryRouter>,
  );

  return onClose;
};

const option = (nodeId: string) =>
  document.querySelector<HTMLButtonElement>(`[data-candidate="${nodeId}"]`)!;

/** Picks a device and goes on to its rule editor. */
const pick = async (nodeId: string) => {
  await waitFor(() => expect(option(nodeId)).not.toBeNull());
  fireEvent.click(option(nodeId));
  fireEvent.click(screen.getByTestId("data-sync-wizard-next"));
};
/** Answers the question the rule editor asks inside the wizard. */
const confirm = () =>
  act(async () => {
    fireEvent.click(screen.getByTestId("data-sync-review-confirm-yes"));
  });

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(dataSyncApi.peers).mockImplementation(async (discover) =>
    discover ? found : found.slice(0, 1),
  );
});
afterEach(cleanup);

describe("sync with another device", () => {
  it("asks inside the dialog, keeps the keyboard there, and tells the page what came of it", async () => {
    vi.mocked(dataSyncApi.createLink).mockResolvedValue({
      link: link(20, "node-new", "New PC", { state: DataSyncLinkState.AwaitingAccess }),
      requestId: "req-out-2",
    });
    const onClose = open();

    await pick("node-new");
    // The rule editor for the device, and no step of the wizard's own.
    expect(screen.getByTestId("data-sync-rule-drawing")).toBeInTheDocument();
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    const dialog = screen.getByRole("dialog");

    expect(within(dialog).getByTestId("data-sync-review-confirm")).toHaveTextContent(
      "dataSync.request.follow This PC",
    );
    const yes = screen.getByTestId("data-sync-review-confirm-yes");

    act(() => yes.focus());
    // The browser takes focus off the button once it is disabled while the link is made.
    const letGo = blurWhenDisabled();

    try {
      await confirm();
    } finally {
      letGo();
    }
    // Asked where it was listed: the request goes to that address.
    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "node-new",
      address: "192.168.1.40:34567",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(onClose).toHaveBeenCalledWith({
      nodeId: "node-new",
      notice: "dataSync.wizard.requested New PC",
    });
    expect(within(dialog).getByRole("heading")).toHaveFocus();
  });

  it("says what each device can do, and lets only those pick that can be linked", async () => {
    open();
    await waitFor(() => expect(option("node-new")).not.toBeNull());

    expect(option("node-nas")).toHaveAttribute("data-status", "linked");
    expect(option("node-old")).toHaveAttribute("data-status", "tooOld");
    expect(option("node-lib")).toHaveAttribute("data-status", "notSharing");
    expect(option("node-new")).toHaveAttribute("data-status", "asks");
    expect(option("node-read")).toHaveAttribute("data-status", "readable");
    expect(option("node-lib")).toHaveAttribute("aria-disabled", "true");
    // A plain list of buttons: the one chosen is pressed, and nothing claims a listbox.
    expect(option("node-new")).toHaveAttribute("aria-pressed", "false");
    expect(screen.queryByRole("listbox")).toBeNull();
    fireEvent.click(option("node-lib"));
    expect(screen.getByTestId("data-sync-wizard-next")).toBeDisabled();
    expect(screen.getByText("dataSync.wizard.notListed")).toBeInTheDocument();
  });

  it("keeps in step both ways with a device found only nearby, at the address it answered at", async () => {
    vi.mocked(dataSyncApi.peers).mockImplementation(async (discover) =>
      discover
        ? [candidate("node-near", "Near PC", { known: false, address: "http://127.0.0.1:62455" })]
        : [],
    );
    vi.mocked(dataSyncApi.createLink).mockResolvedValue({
      link: link(23, "node-near", "Near PC", { state: DataSyncLinkState.AwaitingAccess }),
      requestId: "req-out-3",
    });
    open();
    await pick("node-near");
    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    expect(screen.getByTestId("data-sync-review-confirm")).toHaveTextContent(
      "dataSync.twoWay.consent Near PC",
    );
    await confirm();

    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      peerNodeId: "node-near",
      address: "http://127.0.0.1:62455",
      mode: DataSyncLinkMode.TwoWay,
      kinds: ["customProperty", "extensionGroup"],
    });
  });

  it("turns sharing off again when the request it turned sharing on for fails", async () => {
    vi.mocked(dataSyncApi.createLink).mockRejectedValue(
      new DataSyncProblemError({ code: DataSyncProblemCode.PeerUnreachable }),
    );
    const onClose = open(true, { sharingEnabled: false });

    await pick("node-new");
    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    await confirm();

    expect(vi.mocked(dataSyncApi.setSharing).mock.calls.map(([input]) => input)).toEqual([
      { enabled: true, enablePairedRemoteAccess: false },
      { enabled: false, enablePairedRemoteAccess: false },
    ]);
    // The failure is said where the wizard is, and nothing moved on.
    expect(within(screen.getByRole("dialog")).getByTestId("data-sync-error")).toBeInTheDocument();
    expect(onClose).not.toHaveBeenCalled();
  });

  it("leaves sharing on that was on already, when only remote access was turned on", async () => {
    vi.mocked(dataSyncApi.createLink).mockRejectedValue(
      new DataSyncProblemError({ code: DataSyncProblemCode.PeerUnreachable }),
    );
    open(true, { remoteAccessMode: RemoteAccessMode.Disabled });
    await pick("node-new");
    fireEvent.click(screen.getByTestId("data-sync-mode-twoWay"));
    await confirm();

    expect(vi.mocked(dataSyncApi.setSharing).mock.calls.map(([input]) => input)).toEqual([
      { enabled: true, enablePairedRemoteAccess: true },
    ]);
  });

  it("copies once from a device it reads already, closing before its first sync opens", async () => {
    vi.mocked(dataSyncApi.copyOnce).mockResolvedValue({
      link: link(21, "node-read", "Read PC", {
        mode: DataSyncLinkMode.Off,
        state: DataSyncLinkState.AwaitingReview,
      }),
    });
    const onClose = open();

    await pick("node-read");
    fireEvent.click(screen.getByTestId("data-sync-copy-once"));
    await confirm();

    expect(dataSyncApi.copyOnce).toHaveBeenCalledWith({
      peerNodeId: "node-read",
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(onClose).toHaveBeenCalledWith({
      nodeId: "node-read",
      notice: "dataSync.wizard.ready Read PC",
    });
  });

  it("reaches a device by address and code where this window may create access", async () => {
    vi.mocked(dataSyncApi.createLink).mockResolvedValue({
      link: link(22, "node-far", "Far PC", { state: DataSyncLinkState.AwaitingReview }),
    });
    const onClose = open();

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
    fireEvent.click(screen.getByTestId("data-sync-mode-follow"));
    await confirm();

    expect(dataSyncApi.createLink).toHaveBeenCalledWith({
      address: "10.0.0.5:34567",
      code: "12345678",
      mode: DataSyncLinkMode.Follow,
      kinds: ["customProperty", "extensionGroup"],
    });
    expect(onClose).toHaveBeenCalledWith({
      nodeId: "node-far",
      notice: "dataSync.wizard.ready Far PC",
    });
  });

  it("offers neither asking nor an address and code where this window may not create access", async () => {
    open(false);
    await waitFor(() => expect(option("node-new")).not.toBeNull());

    expect(option("node-new")).toHaveAttribute("data-status", "manageElsewhere");
    expect(option("node-new")).toHaveAttribute("aria-disabled", "true");
    expect(screen.queryByTestId("data-sync-wizard-by-address")).toBeNull();
    // A device it reads already, and that reads it back, can still be linked both ways.
    await pick("node-read");
    expect(screen.getByTestId("data-sync-mode-twoWay")).not.toBeDisabled();
  });

  it("offers data sync's help", async () => {
    open();

    await waitFor(() => expect(option("node-new")).not.toBeNull());
    expect(screen.getByTestId("help")).toHaveAttribute("data-help", "multiDevice/dataSync");
  });
});
