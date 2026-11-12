import { useTranslation } from "react-i18next";
import type { PostParserTask } from "@/core/models/PostParserTask";
import { Button } from "@/components/bakaui";
import { getDownloadInfo } from "../results";

export function ParsingRecordContext({ task }: { task: PostParserTask }) {
  const { t } = useTranslation();
  const resources = getDownloadInfo(task)?.resources?.length ?? 0;
  const hasSavedResults = Object.keys(task.results ?? {}).length > 0;
  return (
    <section
      className="space-y-2 rounded-lg border border-default-200 bg-default-50 p-3 text-sm"
      aria-label={t("postParser.history.savedResult")}
    >
      <h3 className="font-medium">{t("postParser.history.savedResult")}</h3>
      <p>
        {t(
          hasSavedResults
            ? "postParser.history.resultsRetained"
            : "postParser.history.resultsNotSaved",
        )}
      </p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1 text-xs text-default-500">
        <dt>{t("postParser.history.revision")}</dt>
        <dd>{task.revision ?? "—"}</dd>
        <dt>{t("postParser.history.resourceCount")}</dt>
        <dd>{resources}</dd>
        <dt>{t("postParser.label.completedAt")}</dt>
        <dd>{task.completedAt ? new Date(task.completedAt).toLocaleString() : "—"}</dd>
      </dl>
      <p className="text-xs leading-relaxed text-default-500">
        {t("postParser.history.scopeHint")}
      </p>
    </section>
  );
}

export function ParsingRecordEmptyState({
  task,
  onProcessingHistory,
}: {
  task: PostParserTask;
  onProcessingHistory: () => void;
}) {
  const { t } = useTranslation();
  const hasSavedResults = Object.keys(task.results ?? {}).length > 0;
  return (
    <div className="space-y-3 text-left">
      <p className="font-medium text-foreground">{t("postParser.history.noExecutionDetails")}</p>
      <p className="leading-relaxed">
        {t(
          task.workflowRunId
            ? "postParser.history.originalRunUnavailable"
            : "postParser.history.noExecutionRetained",
          { id: task.workflowRunId },
        )}
      </p>
      {hasSavedResults && (
        <p className="leading-relaxed">{t("postParser.history.canUseSavedResults")}</p>
      )}
      <Button
        size="sm"
        color="primary"
        variant="flat"
        className="h-auto whitespace-normal py-2"
        onPress={onProcessingHistory}
      >
        {t("postParser.history.openProcessing")}
      </Button>
    </div>
  );
}
