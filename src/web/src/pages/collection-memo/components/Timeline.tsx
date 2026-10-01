import type { CollectionMemoTarget, TimelineDomain } from "../helpers";

import { useTranslation } from "react-i18next";

import { getTimelineSegments } from "../helpers";

import { Tooltip } from "@/components/bakaui";

interface Props {
  target: CollectionMemoTarget;
  domain: TimelineDomain;
  formatDate: (value: number | string) => string;
}

const Timeline = ({ target, domain, formatDate }: Props) => {
  const { t } = useTranslation();
  const segments = getTimelineSegments(target.ranges, domain);

  return (
    <div
      aria-label={t<string>("collectionMemo.timeline.label", { name: target.name })}
      className="flex flex-col gap-1"
      role="group"
    >
      <div className="relative h-4 rounded-full bg-default-200">
        {segments.map((segment, index) => {
          const label = t<string>(
            segment.point
              ? "collectionMemo.timeline.point"
              : segment.collected
                ? "collectionMemo.timeline.collectedRange"
                : "collectionMemo.timeline.uncollectedRange",
            { start: formatDate(segment.start), end: formatDate(segment.end) },
          );

          return (
            <Tooltip key={index} content={label}>
              <button
                aria-label={label}
                className={`absolute top-0 h-full rounded-sm border-0 p-0 outline-offset-2 focus-visible:outline-2 focus-visible:outline-primary ${segment.collected ? "bg-success" : "bg-default-200"}`}
                style={{
                  left: `${segment.left}%`,
                  width: segment.point ? "4px" : `${segment.width}%`,
                  transform: segment.point ? "translateX(-50%)" : undefined,
                  zIndex: segment.collected ? 1 : 0,
                }}
                type="button"
              />
            </Tooltip>
          );
        })}
      </div>
      <div className="flex justify-between gap-2 text-xs text-default-500">
        <time dateTime={new Date(domain.start).toISOString()}>{formatDate(domain.start)}</time>
        <time className="text-right" dateTime={new Date(domain.end).toISOString()}>
          {t<string>("collectionMemo.timeline.now", { date: formatDate(domain.end) })}
        </time>
      </div>
    </div>
  );
};

export default Timeline;
