import { useEffect } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineSync } from "react-icons/ai";

import { DATA_SYNC_ROUTE } from "../routes";
import { useDataSyncWindow } from "../hooks/useDataSyncWindow";
import { useDataSyncStore } from "../stores/dataSync";
import { overallStatus, waitingElsewhereLine } from "../viewModels";

import { toneDot } from "./common";

import { Button, Tooltip } from "@/components/bakaui";

/** How often the indicator reads the overview again while nothing pushes it. */
export const INDICATOR_REFRESH_MS = 60_000;

/**
 * Data sync at a glance, next to the notifications: two turning arrows with a dot for how it
 * is going, a bubble with what needs you, and a hollow one while other devices hold decisions
 * nobody has taken there. Its tooltip is the status line; a click opens the page.
 *
 * Hidden while data sync is off — no links, no readers, no requests — and in a window that may
 * not use data sync at all. Driven by the data sync store: the overview, read on mount and
 * every minute, and the hub's status pushes in between.
 */
export default function DataSyncStatusIndicator() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const reachable = useDataSyncWindow();
  const status = useDataSyncStore((state) => state.status);
  const reach = useDataSyncStore((state) => state.reach);
  const load = useDataSyncStore((state) => state.load);
  const usable = (reachable === "allowed" || reachable === "unknown") && reach !== "refused";

  useEffect(() => {
    if (!usable) return;
    void load();
    const timer = setInterval(() => {
      if (typeof document === "undefined" || !document.hidden) void load();
    }, INDICATOR_REFRESH_MS);

    return () => clearInterval(timer);
  }, [usable, load]);

  const line = usable ? overallStatus(t, status) : undefined;

  if (!line || !status) return null;
  const elsewhere = waitingElsewhereLine(t, status);
  const label = elsewhere ? `${line.text}. ${elsewhere}` : line.text;

  return (
    <Tooltip
      content={
        <div className="space-y-0.5 text-xs">
          <p>{line.text}</p>
          {elsewhere && <p className="text-default-500">{elsewhere}</p>}
        </div>
      }
    >
      <Button
        isIconOnly
        aria-label={label}
        className="relative"
        color="default"
        data-level={status.level}
        data-testid="data-sync-indicator"
        variant="light"
        onPress={() => navigate(DATA_SYNC_ROUTE)}
      >
        <AiOutlineSync aria-hidden style={{ fontSize: 20 }} />
        <span
          aria-hidden
          className={`absolute bottom-1.5 right-1.5 h-2 w-2 rounded-full ring-2 ring-background ${toneDot[line.tone]}`}
          data-testid="data-sync-indicator-dot"
          data-tone={line.tone}
        />
        {status.openItems > 0 && (
          <span
            aria-hidden
            className="absolute -right-0.5 -top-0.5 min-w-4 rounded-full bg-warning px-1 text-[10px] font-semibold leading-4 text-warning-foreground"
            data-testid="data-sync-indicator-count"
          >
            {status.openItems > 99 ? "99+" : status.openItems}
          </span>
        )}
        {status.peersNeedingDecisions > 0 && (
          <span
            aria-hidden
            className="absolute -left-0.5 -top-0.5 min-w-4 rounded-full border border-warning bg-background px-1 text-[10px] font-semibold leading-[14px] text-warning-700 dark:text-warning"
            data-testid="data-sync-indicator-elsewhere"
          >
            {status.peersNeedingDecisions > 99 ? "99+" : status.peersNeedingDecisions}
          </span>
        )}
      </Button>
    </Tooltip>
  );
}
