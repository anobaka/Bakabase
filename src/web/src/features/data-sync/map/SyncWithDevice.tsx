import type { ReactNode } from "react";
import type { SyncRuleDrawingProps } from "../components/SyncRuleDrawing";

import { useId, useState } from "react";
import { useTranslation } from "react-i18next";

import SyncRuleDrawing from "../components/SyncRuleDrawing";
import { buttonClass } from "../components/common";
import { newSyncPeer } from "../viewModels";

/*
 * The way to start syncing with a device data sync has nothing to do with yet: a device this one
 * knows, or one found nearby that shares its definitions. The rule editor is shown on request —
 * off, with nothing sent — and choosing how to sync there asks the device, as on the page.
 */

export default function SyncWithDevice({
  peerNodeId,
  peerAddress,
  name,
  editor,
  onCreateCode,
  children,
}: {
  /** Its install id. */
  peerNodeId: string;
  /** Where a request to it goes, when the server may not know it by its id. */
  peerAddress?: string;
  name: string;
  editor: Omit<SyncRuleDrawingProps, "peer" | "onCreateCode">;
  onCreateCode: () => void;
  /** What is said before the way to start. */
  children?: ReactNode;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const regionId = useId();

  return (
    <div className="space-y-3" data-testid="data-sync-start">
      {children}
      <button
        aria-controls={open ? regionId : undefined}
        aria-expanded={open}
        className={buttonClass}
        data-testid="data-sync-start-toggle"
        type="button"
        onClick={() => setOpen((current) => !current)}
      >
        {t("dataSync.map.syncWith", { name })}
      </button>
      {open && (
        <div id={regionId}>
          <SyncRuleDrawing
            {...editor}
            peer={newSyncPeer(peerNodeId, name, peerAddress)}
            onCreateCode={onCreateCode}
          />
        </div>
      )}
    </div>
  );
}
