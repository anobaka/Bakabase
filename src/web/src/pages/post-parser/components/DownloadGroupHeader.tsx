import type { DownloadContentGroup } from "../results";

import { useTranslation } from "react-i18next";

import { EvidencePopover } from "./PostDetails";
import Tooltip from "./PostParserTooltip";

export default function DownloadGroupHeader({
  group,
  resourceCount,
}: {
  group?: DownloadContentGroup;
  resourceCount: number;
}) {
  const { t } = useTranslation();

  return (
    <div className="flex min-w-0 flex-col gap-0.5 text-xs">
      <div className="flex min-w-0 flex-wrap items-center gap-x-1.5 gap-y-1">
        <span className="break-words font-medium text-foreground">
          {group?.title ?? t("postParser.groups.ungrouped")}
        </span>
        {group && (
          <>
            <span
              className={`rounded px-1.5 py-0.5 ${group.kind === "main" ? "bg-primary/10 text-primary" : group.kind === "unknown" ? "bg-warning/10 text-warning-600" : "bg-default-100 text-default-500"}`}
            >
              {t(`postParser.groups.kind.${group.kind}`)}
            </span>
            <Tooltip content={t("postParser.groups.inferredHint")}>
              <button className="text-default-400" type="button">
                {t("postParser.groups.inferred")}
              </button>
            </Tooltip>
          </>
        )}
        <span className="text-default-400">
          {t(
            resourceCount === 1
              ? "postParser.groups.singleSource"
              : "postParser.groups.sourceCount",
            { count: resourceCount },
          )}
        </span>
        <EvidencePopover
          evidence={group?.evidence}
          label={t("postParser.groups.evidence", {
            title: group?.title ?? t("postParser.groups.ungrouped"),
          })}
        />
      </div>
      {group?.summary && <p className="break-words text-default-500">{group.summary}</p>}
    </div>
  );
}
