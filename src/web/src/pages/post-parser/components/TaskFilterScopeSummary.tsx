import type { TaskStatusGroup } from "../taskStatus";

import { useTranslation } from "react-i18next";
import { attentionReasonLabel } from "../attentionReasons";

export interface TaskFilterScope {
  keyword?: string;
  status?: TaskStatusGroup | "all";
  reasonCode?: string;
}

export default function TaskFilterScopeSummary({
  keyword,
  status = "all",
  reasonCode,
}: TaskFilterScope) {
  const { t } = useTranslation();
  return (
    <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-4 gap-y-2 rounded-lg bg-default-100 p-3">
      <dt className="text-default-500">{t("postParser.filters.keyword")}</dt>
      <dd className="whitespace-pre-wrap break-words font-medium">
        {keyword?.trim() || t("postParser.filters.anyKeyword")}
      </dd>
      <dt className="text-default-500">{t("postParser.filters.status")}</dt>
      <dd className="font-medium">
        {t(status === "all" ? "postParser.filters.anyStatus" : `postParser.summary.${status}`)}
      </dd>
      {(status === "attention" || reasonCode) && (
        <>
          <dt className="text-default-500">{t("postParser.filters.reason")}</dt>
          <dd className="font-medium">
            {reasonCode ? attentionReasonLabel(t, reasonCode) : t("postParser.filters.anyReason")}
          </dd>
        </>
      )}
    </dl>
  );
}
