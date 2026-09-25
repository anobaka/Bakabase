import type { ReactNode } from "react";
import type { DataSyncMapView } from "../api";
import type { DataSyncDialogActions } from "../hooks/useDataSyncActions";
import type { SyncRuleDrawingProps } from "../components/SyncRuleDrawing";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { useCanManageDefinitionSharing } from "../hooks/useCanManageDefinitionSharing";
import { useDataSyncStore } from "../stores/dataSync";
import InvitationDialog from "../components/InvitationDialog";

import { useWordedActions } from "./useWordedActions";

import { RemoteAccessMode } from "@/sdk/constants";

/** What the device map gives data sync's sections: its actions, and data sync as it read it. */
export interface DataSyncMapHost {
  /** The map's own actions (`PanelActions` fits): focus, busy state, notices and confirmations. */
  actions: DataSyncDialogActions;
  /** `GET /data-sync/map`; absent until read, or when it could not be. */
  view?: DataSyncMapView;
  /** This device's name, as the map shows it: what the other device is told it is. */
  selfName: string;
  now?: number;
}

/**
 * What every data sync section on the device map shares: the map's actions with data sync's
 * failures in data sync's words, this device's side as the rule editor needs it, and a one-time
 * code for a device, opened from the editor.
 */
export function useMapEditor({ actions: hostActions, view, selfName, now }: DataSyncMapHost) {
  const { t } = useTranslation();
  const actions = useWordedActions(hostActions);
  const canManageHere = useCanManageDefinitionSharing();
  const overview = useDataSyncStore((state) => state.overview);
  const [codeFor, setCodeFor] = useState<{ name: string }>();
  const canManage = canManageHere && (overview?.canManageSharing ?? true);

  const editor: Omit<SyncRuleDrawingProps, "peer" | "onCreateCode"> = {
    actions,
    canManage,
    sharingEnabled: view?.sharingEnabled ?? overview?.sharingEnabled ?? false,
    remoteAccessMode:
      view?.remoteAccessMode ?? overview?.remoteAccessMode ?? RemoteAccessMode.Disabled,
    selfName: overview?.deviceName || selfName || t<string>("dataSync.thisDevice"),
    // The map is not the page: the link's own details are one step away there.
    linkToPage: true,
    // The details beside the map are a column: laid out for it before it can be measured.
    initialWidth: 360,
    now,
  };

  const invitation: ReactNode = codeFor ? (
    <InvitationDialog
      actions={actions}
      forName={codeFor.name}
      now={now}
      onClose={() => setCodeFor(undefined)}
    />
  ) : null;

  return {
    actions,
    canManage,
    editor,
    /** Opens a one-time code for the device of this name. */
    createCode: (name: string) => setCodeFor({ name }),
    invitation,
  };
}
