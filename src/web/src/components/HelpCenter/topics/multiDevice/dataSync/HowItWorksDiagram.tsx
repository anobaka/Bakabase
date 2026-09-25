"use client";

import type { LaneState } from "./shared";
import type { ReactNode } from "react";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineFileText, AiOutlineUndo } from "react-icons/ai";

import { CornerBadge, DevicePair, dsk } from "./shared";

export type HowStopId = "link" | "review" | "inStep" | "needsYou" | "undo";

interface HowStop {
  id: HowStopId;
  toHere: LaneState;
  toThere: LaneState;
  badgeHere?: ReactNode;
  badgeThere?: ReactNode;
}

/**
 * The five stops of a link, each with a small picture of the two devices at that moment.
 * This device (the desktop PC) starts the link, so the first arrow is the laptop's
 * definitions coming here: dashed while the laptop has yet to approve, solid once they
 * flow, and both ways once the two keep each other in step.
 */
export const howStops: HowStop[] = [
  {
    id: "link",
    toHere: "pending",
    toThere: "none",
    badgeThere: (
      <CornerBadge tone="neutral">
        <AiOutlineCheck />
      </CornerBadge>
    ),
  },
  {
    id: "review",
    toHere: "active",
    toThere: "none",
    badgeHere: (
      <CornerBadge tone="neutral">
        <AiOutlineFileText />
      </CornerBadge>
    ),
  },
  { id: "inStep", toHere: "active", toThere: "active" },
  {
    id: "needsYou",
    toHere: "idle",
    toThere: "idle",
    badgeHere: <CornerBadge tone="attention">!</CornerBadge>,
    badgeThere: <CornerBadge tone="attention">!</CornerBadge>,
  },
  {
    id: "undo",
    toHere: "idle",
    toThere: "idle",
    badgeHere: (
      <CornerBadge tone="neutral">
        <AiOutlineUndo />
      </CornerBadge>
    ),
  },
];

/**
 * "How data sync works", read top to bottom. Each stop sets its picture beside its words
 * when there is room and above them when there is not, so it reads in a narrow help dialog.
 * The linking stop names the Device map only where this window has one.
 */
const HowItWorksDiagram = ({ hasMap }: { hasMap: boolean }) => {
  const { t } = useTranslation();
  const titleId = `${useId()}-how`;

  return (
    <figure
      aria-labelledby={titleId}
      className="@container flex flex-col gap-2"
      data-testid="data-sync-how"
    >
      <h4 className="text-sm font-medium" id={titleId}>
        {t(dsk("how.title"))}
      </h4>
      <ol className="flex flex-col gap-2">
        {howStops.map((stop, index) => (
          <li
            key={stop.id}
            className="flex flex-col gap-2 rounded-lg border border-default-200 bg-default-50 p-3 @min-[30rem]:flex-row @min-[30rem]:items-center @min-[30rem]:gap-4"
            data-stop={stop.id}
          >
            <DevicePair
              badgeHere={stop.badgeHere}
              badgeThere={stop.badgeThere}
              toHere={stop.toHere}
              toThere={stop.toThere}
            />
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2 text-sm font-medium">
                <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                  {index + 1}
                </span>
                {t(dsk(`how.${stop.id}.title`))}
              </div>
              <p className="mt-1 text-xs text-default-600">
                {t(
                  dsk(stop.id === "link" && !hasMap ? "how.link.descNoMap" : `how.${stop.id}.desc`),
                )}
              </p>
            </div>
          </li>
        ))}
      </ol>
      <figcaption className="text-xs text-default-500">{t(dsk("how.caption"))}</figcaption>
    </figure>
  );
};

HowItWorksDiagram.displayName = "HowItWorksDiagram";

export default HowItWorksDiagram;
