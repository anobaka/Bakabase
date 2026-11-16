import { useTranslation } from "react-i18next";
import { Chip, Tooltip } from "@/components/bakaui";
import { taskTag } from "./taskTags";

export default function AcquisitionAttentionTags({ tags, facts }: { tags?: string[] | null; facts?: string[] }) {
  const { t } = useTranslation();
  const entries = [
    ...(facts ?? []).map(id => taskTag("facts", id)),
    ...(tags ?? []).map(id => taskTag("attentionTags", id)),
  ];
  if (!entries.length) return null;
  return (
    <div role="list" className="flex flex-wrap gap-1" aria-label={t("acquisition.filters.title")}>
      {entries.map(({ kind, id, label, hint }) => (
        <Tooltip key={`${kind}:${id}`} content={t(hint)} delay={1000} placement="bottom">
          <Chip role="listitem" size="sm" variant="flat"
            color={id === "sourceUnavailable" ? "danger" : id === "downloadedPending" ? "primary" : "warning"}>
            {t(label)}
          </Chip>
        </Tooltip>
      ))}
    </div>
  );
}
