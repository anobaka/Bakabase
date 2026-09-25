import type { DataSyncHistoryEntry } from "../api";
import type { HistorySource } from "../historyModels";

import { useTranslation } from "react-i18next";

import { HISTORY_DRAWING_ROWS, historySources } from "../historyModels";
import { useElementWidth } from "../hooks/useElementWidth";
import { timeAgo } from "../times";

import { syncText } from "./common";

import { edgeStyles } from "@/features/federation/map/DeviceMapCanvas";

/*
 * Above the history (v3.1's sources drawing, adapted): one row per device that sent definitions
 * here in the last 30 days — that device → this device, with how often and what it did — the
 * busiest-recent three, then "+N more". With nothing received, this device alone.
 */

const ROW_H = 44;
const WIDTH = 520;

/** Below this width the rows stack: one line per device, drawn small. */
export const HISTORY_DRAWING_STACK_BELOW = 480;

const truncate = (text: string, max = 18) =>
  text.length > max ? `${text.slice(0, max - 1)}…` : text;

export default function HistoryDrawing({
  entries,
  selfName,
  now,
}: {
  entries: readonly DataSyncHistoryEntry[];
  selfName: string;
  now?: number;
}) {
  const { t } = useTranslation();
  const { ref, width } = useElementWidth<HTMLElement>(WIDTH);
  const sources = historySources(entries, now);
  const rows = sources.slice(0, HISTORY_DRAWING_ROWS);
  const more = sources.length - rows.length;
  const style = edgeStyles.sync;
  const summary = (source: HistorySource) =>
    [
      t("dataSync.history.drawing.syncs", { count: source.syncs }),
      source.created ? t("dataSync.history.count.created", { count: source.created }) : "",
      source.updated ? t("dataSync.history.count.updated", { count: source.updated }) : "",
      source.linked ? t("dataSync.history.count.linked", { count: source.linked }) : "",
      source.deleted ? t("dataSync.history.count.deleted", { count: source.deleted }) : "",
    ]
      .filter(Boolean)
      .join(" · ");

  if (!rows.length)
    return (
      <figure ref={ref} className="flex items-center gap-3" data-testid="data-sync-history-drawing">
        <svg aria-hidden className="h-10 w-28" viewBox="0 0 112 40">
          <rect
            className="fill-primary/10 stroke-primary"
            height="32"
            rx="6"
            width="104"
            x="4"
            y="4"
          />
          <text className="fill-foreground text-[11px]" textAnchor="middle" x="56" y="24">
            {truncate(selfName, 14)}
          </text>
        </svg>
        <figcaption className="text-sm text-default-500">
          {t("dataSync.history.drawing.empty")}
        </figcaption>
      </figure>
    );

  if (width < HISTORY_DRAWING_STACK_BELOW)
    return (
      <figure
        ref={ref}
        className="space-y-1.5"
        data-mode="stacked"
        data-testid="data-sync-history-drawing"
      >
        <ul className="space-y-1.5">
          {rows.map((source) => (
            <li
              key={source.nodeId}
              className="rounded-lg bg-default-50 px-2 py-1.5 text-xs"
              data-source={source.nodeId}
            >
              <p className="flex items-center gap-1.5 font-medium">
                <span className="min-w-0 truncate">{source.name}</span>
                <span aria-hidden className={syncText}>
                  →
                </span>
                <span className="min-w-0 truncate">{selfName}</span>
              </p>
              <p className="text-default-500">
                {summary(source)} · {timeAgo(t, source.lastAt, now)}
              </p>
            </li>
          ))}
        </ul>
        {more > 0 && (
          <figcaption className="text-xs text-default-500">
            {t("dataSync.history.drawing.more", { count: more })}
          </figcaption>
        )}
      </figure>
    );

  return (
    <figure
      ref={ref}
      className="space-y-1"
      data-mode="drawing"
      data-testid="data-sync-history-drawing"
    >
      <svg
        aria-label={t("dataSync.history.drawing.label")}
        className="w-full max-w-xl"
        role="img"
        viewBox={`0 0 ${WIDTH} ${rows.length * ROW_H + 4}`}
      >
        <defs>
          <marker
            id="data-sync-history-arrow"
            markerHeight="8"
            markerWidth="8"
            orient="auto"
            refX="7"
            refY="4"
          >
            <path className={style.fill} d="M0 0 L8 4 L0 8 Z" />
          </marker>
        </defs>
        {rows.map((source, index) => {
          const y = index * ROW_H + 4;

          return (
            <g key={source.nodeId} data-source={source.nodeId}>
              <rect
                className="fill-content2 stroke-default-300"
                height={30}
                rx={6}
                width={120}
                x={4}
                y={y}
              />
              <text className="fill-foreground text-[11px]" textAnchor="middle" x={64} y={y + 19}>
                {truncate(source.name)}
              </text>
              <line
                className={style.stroke}
                markerEnd="url(#data-sync-history-arrow)"
                strokeWidth={style.width}
                x1={128}
                x2={392}
                y1={y + 15}
                y2={y + 15}
              />
              <text className="fill-default-500 text-[10px]" textAnchor="middle" x={260} y={y + 10}>
                {truncate(summary(source), 48)}
              </text>
              <text className="fill-default-500 text-[10px]" textAnchor="middle" x={260} y={y + 28}>
                {timeAgo(t, source.lastAt, now)}
              </text>
              <rect
                className="fill-primary/10 stroke-primary"
                height={30}
                rx={6}
                width={116}
                x={400}
                y={y}
              />
              <text className="fill-foreground text-[11px]" textAnchor="middle" x={458} y={y + 19}>
                {truncate(selfName, 16)}
              </text>
            </g>
          );
        })}
      </svg>
      <figcaption className="space-y-0.5 text-xs text-default-500">
        <ul className="sr-only">
          {rows.map((source) => (
            <li key={source.nodeId}>
              {t("dataSync.history.drawing.row", { name: source.name, summary: summary(source) })}
            </li>
          ))}
        </ul>
        {more > 0 && <span>{t("dataSync.history.drawing.more", { count: more })}</span>}
      </figcaption>
    </figure>
  );
}
