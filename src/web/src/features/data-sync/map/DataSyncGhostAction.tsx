import type { DataSyncMapHost } from "./useMapEditor";

import { useTranslation } from "react-i18next";

import { useMapEditor } from "./useMapEditor";
import SyncWithDevice from "./SyncWithDevice";

/*
 * A device found nearby, in the device map's details (spec §11.1, hook H-map-panel): one that
 * says it shares its definitions can be synced with from here; any other is told how to let it,
 * never left out — which is what a reader looking for it needs to know.
 */

/**
 * What library sharing's discovery answered for it: the map's `SharingCandidate` fits, and an
 * install new enough for data sync also says whether it shares its definitions.
 */
export interface DataSyncGhostCandidate {
  nodeId: string;
  address: string;
  sharesDefinitions?: boolean | null;
}

export interface DataSyncGhostActionProps extends DataSyncMapHost {
  node: { name: string; sources: { sharingCandidate?: DataSyncGhostCandidate } };
}

export default function DataSyncGhostAction({ node, ...host }: DataSyncGhostActionProps) {
  const { t } = useTranslation();
  const { editor, createCode, invitation } = useMapEditor(host);
  const candidate = node.sources.sharingCandidate;
  const name = node.name;

  return (
    <div className="space-y-2" data-testid="data-sync-ghost">
      {candidate?.sharesDefinitions ? (
        <SyncWithDevice
          editor={editor}
          name={name}
          peerAddress={candidate.address}
          peerNodeId={candidate.nodeId}
          onCreateCode={() => createCode(name)}
        >
          <p className="text-xs text-default-500">{t("dataSync.map.ghost.shares")}</p>
        </SyncWithDevice>
      ) : (
        <p className="text-xs text-default-500" data-testid="data-sync-ghost-guidance">
          {t("dataSync.map.ghost.notSharing")}
        </p>
      )}
      {invitation}
    </div>
  );
}
