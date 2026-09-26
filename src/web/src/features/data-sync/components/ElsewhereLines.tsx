import type { SyncPeer } from "../viewModels";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { useOpenPeerDataSync } from "../hooks/useOpenPeerDataSync";
import { elsewhereLines } from "../viewModels";

import { smallButtonClass } from "./common";

/*
 * "Waiting on other devices" (spec §9.1 N): one line per device whose own decisions nobody has
 * taken there — typically a headless hub, which holds back what it has not decided. Decided
 * there: the button switches the window to it, where this window manages it; otherwise the line
 * says how to get there.
 */

/**
 * Where decisions another device holds are taken, where this window cannot switch to it: on
 * that device, in its own window — and, where this window has the Devices page to pair from
 * (`pairable`, from {@link useOpenPeerDataSync}), how it could switch to it.
 */
export function DecideThereHint({ name, pairable }: { name: string; pairable: boolean }) {
  const { t } = useTranslation();

  return (
    <span className="text-default-500" data-testid="data-sync-decide-there">
      {t("dataSync.link.decideThere", { name })}
      {pairable
        ? ` ${t("dataSync.link.switchNeedsManagement", {
            name,
            mode: t("federation.mode"),
            page: t("federation.devices.title"),
          })}`
        : ""}
    </span>
  );
}

export default function ElsewhereLines({ peers }: { peers: SyncPeer[] }) {
  const { t } = useTranslation();
  const lines = elsewhereLines(t, peers);
  const { canOpen, open, canPair } = useOpenPeerDataSync(lines.length > 0);
  const [error, setError] = useState<string>();

  if (!lines.length) return null;

  return (
    <div className="space-y-1.5" data-testid="data-sync-elsewhere">
      <p className="text-xs font-medium text-default-500">{t("dataSync.inbox.elsewhere.title")}</p>
      <ul className="space-y-1.5">
        {lines.map((line) => (
          <li
            key={line.nodeId}
            className="flex flex-wrap items-center gap-2 rounded-lg border border-warning/30 bg-warning/5 px-2.5 py-1.5 text-sm"
            data-peer={line.nodeId}
          >
            <span className="min-w-0 flex-1">{line.text}</span>
            {canOpen(line.nodeId) ? (
              <button
                className={smallButtonClass}
                data-testid="data-sync-elsewhere-open"
                type="button"
                onClick={() =>
                  open(line.nodeId).catch(() =>
                    setError(t("dataSync.inbox.elsewhere.openFailed", { name: line.name })),
                  )
                }
              >
                {t("dataSync.inbox.elsewhere.open", { name: line.name })}
              </button>
            ) : (
              <span className="text-xs">
                <DecideThereHint name={line.name} pairable={canPair} />
              </span>
            )}
          </li>
        ))}
      </ul>
      {error && (
        <p className="text-xs text-danger-700" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
