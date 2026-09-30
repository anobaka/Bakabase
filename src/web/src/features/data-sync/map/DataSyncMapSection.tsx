import type { DataSyncMapPeer, DataSyncMapRequest } from "../api";
import type { DataSyncMapHost } from "./useMapEditor";

import { useId } from "react";
import { useTranslation } from "react-i18next";

import SyncRuleDrawing from "../components/SyncRuleDrawing";
import DataSyncHelp from "../components/DataSyncHelp";
import { syncText } from "../components/common";
import { requestEnded } from "../viewModels";

import DataSyncOutgoingCard from "./DataSyncOutgoingCard";
import DataSyncRequestCard from "./DataSyncRequestCard";
import { installIdOf, syncPeerOfSources } from "./mapAdapter";
import { useMapEditor } from "./useMapEditor";
import SyncWithDevice from "./SyncWithDevice";

import { KindBadge } from "@/features/federation/map/DeviceMapCanvas";

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
  };
}

export interface DataSyncMapSectionProps extends DataSyncMapHost {
  node: DataSyncMapSectionNode;
}

export default function DataSyncMapSection({ node, ...host }: DataSyncMapSectionProps) {
  const { t } = useTranslation();
  const headingId = useId();
  const { actions, canManage, editor, createCode, invitation } = useMapEditor(host);
  const { syncRequests } = node.sources;
  const name = node.name;
  const installId = installIdOf(node.keys);
  // Where a request to it goes — never an address another server answers at.
  const reachableAt = node.issues.includes("wrongServer") ? undefined : node.address;
  const record = syncPeerOfSources(node.sources);
  const peer = record && { ...record, name, address: record.address ?? reachableAt };

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
          sharingEnabled={editor.sharingEnabled}
        />
      ));
    const card = peer?.outcome && (
      <DataSyncOutgoingCard actions={actions} now={host.now} peer={peer} />
    );

    // Its request ended: read and dismissed before anything else is done with it. Only the
    // request: the link has no mode to show until it is approved.
    if (peer && (requestEnded(peer) || (peer.linkId === undefined && !peer.peerMayRead)))
      return card;
    if (peer)
      return (
        <>
          {card}
          <SyncRuleDrawing {...editor} peer={peer} onCreateCode={() => createCode(name)} />
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
      <div className="flex items-center justify-between gap-2">
        <h3 className={`flex items-center gap-2 text-sm font-semibold ${syncText}`} id={headingId}>
          <KindBadge kind="sync" />
          {t("federation.map.edge.sync")}
        </h3>
        <DataSyncHelp />
      </div>
      {body()}
      {invitation}
    </section>
  );
}
