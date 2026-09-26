import type * as Api from "../api";

import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DataSyncRequestCard from "../map/DataSyncRequestCard";
import { dataSyncApi } from "../api";

import { link, NOW, recordingActions, request } from "./dataSyncFixtures";

import {
  DataSyncLinkInitiator,
  DataSyncLinkState,
  DataSyncRequestIntent,
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
    approveRequest: vi.fn(async () => ({ readBackGranted: false })),
    rejectRequest: vi.fn(async () => undefined),
    setSharing: vi.fn(async () => undefined),
  },
}));

let recorded = recordingActions();

const card = (
  item = request("req-in-1", "node-newpc", "New PC"),
  options: {
    canManage?: boolean;
    sharingEnabled?: boolean;
    remoteAccessMode?: RemoteAccessMode;
  } = {},
) =>
  render(
    <DataSyncRequestCard
      actions={recorded.actions}
      canManage={options.canManage ?? true}
      now={NOW}
      remoteAccessMode={options.remoteAccessMode ?? RemoteAccessMode.Enabled}
      request={item}
      sharingEnabled={options.sharingEnabled ?? true}
    />,
  );

const confirmation = () => recorded.confirmations[recorded.confirmations.length - 1];

beforeEach(() => {
  vi.clearAllMocks();
  recorded = recordingActions();
});
afterEach(cleanup);

describe("a request to read this device's definitions", () => {
  it("says where it really came from, and to approve only your own devices", () => {
    card(request("req-in-1", "node-newpc", "New PC", { intent: DataSyncRequestIntent.Follow }));

    expect(screen.getByText("dataSync.request.follow New PC")).toBeInTheDocument();
    expect(screen.getByTestId("data-sync-request-from")).toHaveTextContent(
      "dataSync.request.from 192.168.1.40",
    );
    expect(screen.queryByTestId("data-sync-request-claim")).toBeNull();
  });

  it("says so when nothing tells where it came from", () => {
    card(request("req-in-1", "node-newpc", "New PC", { remoteAddress: undefined }));

    expect(screen.getByTestId("data-sync-request-from")).toHaveTextContent(
      "dataSync.request.from dataSync.request.unknownAddress",
    );
  });

  it("warns when it claims to be a device this one knows at another address", () => {
    card(
      request("req-in-2", "node-nas", "NAS", {
        intent: DataSyncRequestIntent.Follow,
        remoteAddress: "192.168.1.99",
        claimsKnownDevice: true,
        knownAddress: "192.168.1.10:34567",
      }),
    );

    expect(screen.getByTestId("data-sync-request-claim")).toHaveTextContent(
      "dataSync.request.claim NAS 192.168.1.10:34567 192.168.1.99",
    );
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    // The confirmation says it too: it only has text.
    expect(confirmation().warning).toContain("dataSync.request.claim NAS");
    expect(screen.queryByTestId("data-sync-request-replaces")).toBeNull();
  });

  it("warns that approving replaces the access of a device that already reads under its id", () => {
    // No claim warning: a device known by a host name is never flagged as elsewhere.
    card(
      request("req-in-2", "node-nas", "NAS", {
        intent: DataSyncRequestIntent.Follow,
        replacesExistingAccess: true,
      }),
    );

    expect(screen.queryByTestId("data-sync-request-claim")).toBeNull();
    expect(screen.getByTestId("data-sync-request-replaces")).toHaveTextContent(
      "dataSync.request.replacesExisting",
    );
    // A Follow request reads nothing back.
    expect(screen.getByTestId("data-sync-request-replaces")).not.toHaveTextContent(
      "replacesExistingReadBack",
    );
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).toBe(
      "dataSync.request.from 192.168.1.40 dataSync.request.replacesExisting",
    );
  });

  it("says receiving back reads that id from the request's address, while it is ticked", () => {
    card(request("req-in-2", "node-nas", "NAS", { replacesExistingAccess: true }));
    const replaces = screen.getByTestId("data-sync-request-replaces");

    expect(replaces).toHaveTextContent(
      "dataSync.request.replacesExisting dataSync.request.replacesExistingReadBack",
    );
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).toContain("dataSync.request.replacesExistingReadBack");

    fireEvent.click(screen.getByTestId("data-sync-request-receive-back"));
    expect(replaces).not.toHaveTextContent("replacesExistingReadBack");
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).toBe(
      "dataSync.request.from 192.168.1.40 dataSync.request.replacesExisting",
    );
  });

  it("says nothing of replacing where no device reads under its id", () => {
    card();

    expect(screen.queryByTestId("data-sync-request-replaces")).toBeNull();
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).not.toContain("replacesExisting");
  });

  it("offers receiving back and the kinds inline, before approving", async () => {
    vi.mocked(dataSyncApi.approveRequest).mockResolvedValueOnce({
      readBackGranted: true,
      createdLink: link(7, "node-newpc", "New PC", {
        state: DataSyncLinkState.WaitingForPeerReview,
        initiator: DataSyncLinkInitiator.Peer,
      }),
    });
    card();
    const options = screen.getByTestId("data-sync-request-options");
    const receiveBack = within(options).getByTestId("data-sync-request-receive-back");

    expect(receiveBack).toBeChecked();
    const kinds = within(options).getAllByTestId("data-sync-request-kind");

    expect(kinds.map((kind) => kind.dataset.kind)).toEqual(["customProperty", "extensionGroup"]);
    fireEvent.click(kinds[0]);
    // The last kind stays on.
    fireEvent.click(kinds[1]);
    expect(kinds[1]).toBeChecked();

    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation()).toMatchObject({
      title: "dataSync.request.approveTitle New PC",
      description: "dataSync.request.twoWay New PC",
      warning: "dataSync.request.from 192.168.1.40",
      refresh: ["dataSync", "sharing"],
    });
    expect(dataSyncApi.approveRequest).not.toHaveBeenCalled();
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.approveRequest).toHaveBeenCalledWith("req-in-1", {
      receiveBack: true,
      kinds: ["extensionGroup"],
    });
    // The card goes with the request; what the approval said stays with the host.
    expect(recorded.actions.setNotice).toHaveBeenCalledWith(
      "dataSync.request.approvedBothWays New PC",
    );
  });

  it("says why reading the other device back failed, never that it receives from it", async () => {
    // What the server answers: the grant stands, the link waits for access, and its detail says why.
    vi.mocked(dataSyncApi.approveRequest).mockResolvedValueOnce({
      readBackGranted: false,
      createdLink: link(7, "node-newpc", "New PC", {
        state: DataSyncLinkState.AwaitingAccess,
        initiator: DataSyncLinkInitiator.Peer,
        lastErrorCode: "ReadBackFailed",
        lastErrorDetail: "Unreachable",
      }),
    });
    card();
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    await act(() => confirmation().action() as Promise<void>);

    expect(recorded.actions.setNotice).toHaveBeenCalledWith(
      "dataSync.request.approvedReadBackFailed New PC dataSync.peerError.Unreachable",
    );
  });

  it("approves without receiving back when that is unticked", async () => {
    card();
    fireEvent.click(screen.getByTestId("data-sync-request-receive-back"));
    expect(screen.queryAllByTestId("data-sync-request-kind")).toHaveLength(0);
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.approveRequest).toHaveBeenCalledWith("req-in-1", {
      receiveBack: false,
      kinds: undefined,
    });
    expect(recorded.actions.setNotice).toHaveBeenCalledWith("dataSync.request.approved New PC");
  });

  it.each([
    ["remote access", true, RemoteAccessMode.Disabled],
    ["sharing", false, RemoteAccessMode.Enabled],
    ["sharing and remote access", false, RemoteAccessMode.Disabled],
  ])(
    "turns %s on, naming each, when approving needs it",
    async (_, sharingEnabled, remoteAccessMode) => {
      card(request("req-in-1", "node-newpc", "New PC", { intent: DataSyncRequestIntent.Follow }), {
        sharingEnabled,
        remoteAccessMode,
      });
      fireEvent.click(screen.getByTestId("data-sync-request-approve"));
      const warning = confirmation().warning ?? "";

      expect(warning.includes("dataSync.twoWay.turnsOnSharing")).toBe(!sharingEnabled);
      expect(warning.includes("dataSync.sharing.remoteAccess")).toBe(
        remoteAccessMode === RemoteAccessMode.Disabled,
      );
      await act(() => confirmation().action() as Promise<void>);
      expect(dataSyncApi.setSharing).toHaveBeenCalledWith({
        enabled: true,
        enablePairedRemoteAccess: true,
      });
      expect(dataSyncApi.approveRequest).toHaveBeenCalled();
    },
  );

  it("turns nothing on, and says nothing of it, where both are on", async () => {
    card();
    fireEvent.click(screen.getByTestId("data-sync-request-approve"));
    expect(confirmation().warning).toBe("dataSync.request.from 192.168.1.40");
    await act(() => confirmation().action() as Promise<void>);
    expect(dataSyncApi.setSharing).not.toHaveBeenCalled();
  });

  it("rejects through the host, from any window", async () => {
    card(undefined, { canManage: false });

    expect(screen.queryByTestId("data-sync-request-approve")).toBeNull();
    expect(screen.queryByTestId("data-sync-request-options")).toBeNull();
    expect(screen.getByText("dataSync.manageElsewhere")).toBeInTheDocument();
    await act(async () => {
      fireEvent.click(screen.getByTestId("data-sync-request-reject"));
    });
    expect(recorded.actions.run).toHaveBeenCalledTimes(1);
    expect(dataSyncApi.rejectRequest).toHaveBeenCalledWith("req-in-1");
  });

  it("is only the request: it never shows the rule editor", () => {
    card();

    expect(screen.getByTestId("data-sync-request-card")).toBeInTheDocument();
    expect(screen.queryByTestId("data-sync-rule-drawing")).toBeNull();
    expect(screen.queryByTestId("data-sync-arrow-receive")).toBeNull();
  });
});
