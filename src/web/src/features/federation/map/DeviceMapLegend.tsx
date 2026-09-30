import type { DeviceGraph, MapEdgeKind } from "./graph";

import { useTranslation } from "react-i18next";

import { ATTENTION_DASH, AttentionMark, edgeStyles, KindBadge } from "./DeviceMapCanvas";

/** A short line with an arrowhead, in a relationship kind's colour. */
const LineSwatch = ({
  kind,
  dashed = false,
}: {
  kind: MapEdgeKind | "neutral";
  dashed?: boolean;
}) => {
  const stroke = kind === "neutral" ? "stroke-default-500" : edgeStyles[kind].stroke;
  const fill = kind === "neutral" ? "fill-default-500" : edgeStyles[kind].fill;

  return (
    <svg aria-hidden className="h-3 w-8 shrink-0" viewBox="0 0 32 12">
      <path
        className={stroke}
        d="M1 6 H25"
        strokeDasharray={dashed ? "4 3" : undefined}
        strokeWidth={kind === "management" ? 2.5 : 2}
      />
      <path className={fill} d="M23 1.5 L31 6 L23 10.5 z" />
    </svg>
  );
};

const Dot = ({ className }: { className: string }) => (
  <svg aria-hidden className="h-2.5 w-2.5 shrink-0" viewBox="0 0 10 10">
    <circle className={className} cx={5} cy={5} r={4} strokeWidth={1.5} />
  </svg>
);

/**
 * What the lines and marks mean. A relationship kind nothing on the map uses (data sync, until
 * something syncs) is left out, so the legend never explains a line that is not there.
 */
export default function DeviceMapLegend({ graph }: { graph: DeviceGraph }) {
  const { t } = useTranslation();
  const shown = new Set(graph.edges.map((edge) => edge.kind));
  const kinds: MapEdgeKind[] = [
    "sharing",
    "management",
    ...(shown.has("sync") ? (["sync"] as const) : []),
  ];

  return (
    <ul
      aria-label={t("federation.map.legend.title")}
      className="flex flex-wrap items-center gap-x-5 gap-y-2 px-2 pb-1 text-xs text-default-500"
      data-testid="device-map-legend"
    >
      {kinds.map((kind) => (
        <li key={kind} className="flex items-center gap-1.5" data-legend={kind}>
          <KindBadge className="h-4 w-4" kind={kind} />
          <LineSwatch kind={kind} />
          {t(`federation.map.legend.${kind}`)}
        </li>
      ))}
      <li className="flex items-center gap-1.5" data-legend="pending">
        <LineSwatch dashed kind="neutral" />
        {t("federation.map.legend.pending")}
      </li>
      <li className="flex items-center gap-1.5" data-legend="attention">
        <svg aria-hidden className="h-3.5 w-10 shrink-0" viewBox="0 0 40 14">
          <path
            className="stroke-default-500"
            d="M1 7 H25"
            strokeDasharray={ATTENTION_DASH}
            strokeLinecap="round"
            strokeWidth={2.2}
          />
          <path className="fill-default-500" d="M23 2.5 L31 7 L23 11.5 z" />
          <AttentionMark x={35} y={7} />
        </svg>
        {t("federation.map.legend.attention")}
      </li>
      <li className="flex items-center gap-1.5" data-legend="unverified">
        <svg aria-hidden className="h-3.5 w-3.5 shrink-0" viewBox="-8 -8 16 16">
          <circle className="fill-warning/15 stroke-warning" r={7} strokeWidth={1.3} />
          <text
            className="fill-warning-700 dark:fill-warning"
            dominantBaseline="central"
            fontSize={10}
            fontWeight={700}
            textAnchor="middle"
          >
            ?
          </text>
        </svg>
        {t("federation.map.legend.unverified")}
      </li>
      <li className="flex items-center gap-1.5">
        <Dot className="fill-success stroke-success" />
        {t("federation.map.presence.online")}
      </li>
      <li className="flex items-center gap-1.5">
        <Dot className="fill-default-400 stroke-default-400" />
        {t("federation.map.presence.offline")}
      </li>
      <li className="flex items-center gap-1.5">
        <Dot className="fill-content1 stroke-default-400" />
        {t("federation.map.presence.unknown")}
      </li>
      <li className="flex items-center gap-1.5" data-legend="ghost">
        <svg aria-hidden className="h-3 w-5 shrink-0" viewBox="0 0 20 12">
          <rect
            className="fill-none stroke-default-400"
            height={10}
            rx={3}
            strokeDasharray="3 2"
            width={18}
            x={1}
            y={1}
          />
        </svg>
        {t("federation.map.legend.ghost")}
      </li>
    </ul>
  );
}
