import type { DataSyncMapOutgoing, DataSyncMapPeer, DataSyncMapRequest } from "../api";
import type { SyncOutcome } from "../viewModels";
import type { DataSyncMapHost } from "./useMapEditor";

import { useId } from "react";
import { useTranslation } from "react-i18next";

import SyncRuleDrawing from "../components/SyncRuleDrawing";

import DataSyncOutgoingCard from "./DataSyncOutgoingCard";
import DataSyncRequestCard from "./DataSyncRequestCard";
import { installIdOf, isEnded, syncPeerOfSources } from "./mapAdapter";
import { useMapEditor } from "./useMapEditor";
import SyncWithDevice from "./SyncWithDevice";

import { edgeStyles, KindBadge } from "@/features/federation/map/DeviceMapCanvas";

/*
 * Data sync with one device, in the device map's details (spec §11.1, hook H-map-panel):
 *
 * - a device known only from its own request — a claim — shows that request and nothing else:
 *   never the rule editor, whose arrows would act on a device nobody verified;
 * - this device's own request to it, waiting with [Cancel] or ended with [Dismiss], until it is
 *   dismissed;
 * - a device this one syncs with — a link, or a grant to read this device — the rule editor;
 * - a device it does not sync with yet, known by its install id, the way to start.
 *
 * Everything it does goes through the map's actions: focus and busy state stay the map's.
 */

/** What the section reads of a map node: the map's `MapNode` fits it. */
export interface DataSyncMapSectionNode {
  id: string;
  name: string;
  address?: string;
  unverified: boolean;
  keys: readonly string[];
  issues: readonly string[];
  sources: {
    sync?: DataSyncMapPeer;
    syncRequests: readonly DataSyncMapRequest[];
    syncOutgoing: readonly DataSyncMapOutgoing[];
  };
}

export interface DataSyncMapSectionProps extends DataSyncMapHost {
  node: DataSyncMapSectionNode;
}

const outcomeOf = (outgoing: DataSyncMapOutgoing): SyncOutcome =>
  !isEnded(outgoing)
    ? "awaitingApproval"
    : outgoing.outcome === "rejected"
      ? "rejected"
      : "expired";

export default function DataSyncMapSection({ node, ...host }: DataSyncMapSectionProps) {
  const { t } = useTranslation();
  const headingId = useId();
  const { actions, canManage, editor, createCode, invitation } = useMapEditor(host);
  const { sync, syncRequests, syncOutgoing } = node.sources;
  const name = node.name;
  const installId = installIdOf(node.keys);
  const outgoing = syncOutgoing[syncOutgoing.length - 1];
  const peer = syncPeerOfSources(node.sources);
  // Where a request to it goes — never an address another server answers at.
  const reachableAt = node.issues.includes("wrongServer") ? undefined : node.address;

  if (node.unverified ? !syncRequests.length : !peer && !installId) return null;

  const body = () => {
    if (node.unverified)
      return syncRequests.map((request) => (
        <DataSyncRequestCard
          key={request.requestId}
          actions={actions}
          canManage={canManage}
          now={host.now}
          remoteAccessMode={editor.remoteAccessMode}
          request={request}
        />
      ));
    const ended = !!outgoing && isEnded(outgoing);
    const card = outgoing && (
      <DataSyncOutgoingCard
        actions={actions}
        address={outgoing.address}
        expiresAt={outgoing.expiresAt}
        linkId={outgoing.linkId}
        nodeName={name}
        now={host.now}
        outcome={outcomeOf(outgoing)}
      />
    );

    // Its request ended: read and dismissed before anything else is done with it.
    if (ended) return card;
    // Only the request: the link has no mode to show until it is approved.
    if (!sync && outgoing) return card;
    if (peer)
      return (
        <>
          {card}
          <SyncRuleDrawing
            {...editor}
            peer={{ ...peer, name, address: peer.address ?? reachableAt }}
            onCreateCode={() => createCode(name)}
          />
        </>
      );

    return (
      <SyncWithDevice
        editor={editor}
        name={name}
        peerAddress={reachableAt}
        peerNodeId={installId!}
        onCreateCode={() => createCode(name)}
      >
        <p className="text-sm text-default-500">{t("dataSync.map.none", { name })}</p>
      </SyncWithDevice>
    );
  };

  return (
    <section
      aria-labelledby={headingId}
      className="space-y-3 border-t border-default-200 pt-4"
      data-testid="device-map-sync-section"
    >
      <h3
        className={`flex items-center gap-2 text-sm font-semibold ${edgeStyles.sync.text}`}
        id={headingId}
      >
        <KindBadge kind="sync" />
        {t("federation.map.edge.sync")}
      </h3>
      {body()}
      {invitation}
    </section>
  );
}
