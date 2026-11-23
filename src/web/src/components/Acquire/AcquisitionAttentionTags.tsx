import { useTranslation } from "react-i18next";
import { Button, Chip, Tooltip } from "@/components/bakaui";
import { taskTag } from "./taskTags";

export default function AcquisitionAttentionTags({ tags, facts, onReviewCleanup }: {
  tags?: string[] | null;
  facts?: string[];
  onReviewCleanup?: () => void;
}) {
  const { t } = useTranslation();
  const waitingForFiles = facts?.includes("awaitingFileRelease");
  const entries = [
    ...(facts ?? []).filter(id => id !== "fileCleanupNeedsReview" || !waitingForFiles)
      .map(id => taskTag("facts", id)),
    ...(tags ?? []).map(id => taskTag("attentionTags", id)),
  ];
  if (!entries.length) return null;
  return (
    <div role="list" className="flex flex-wrap gap-1" aria-label={t("acquisition.filters.title")}>
      {entries.map(({ kind, id, label, hint }) => kind === "facts" && id === "fileCleanupNeedsReview" && onReviewCleanup ? (
        <span key={`${kind}:${id}`} role="listitem">
          <Tooltip content={t(hint)} delay={1000} placement="bottom">
            <Button size="sm" variant="flat" color="warning" className="h-6 px-2" onPress={onReviewCleanup}>
              {t(label)}
            </Button>
          </Tooltip>
        </span>
      ) : (
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
