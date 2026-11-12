import { useCallback, useMemo } from "react";
import { useTranslation } from "react-i18next";
import WorkflowRunsDrawer from "@/components/Workflow/WorkflowRunsDrawer";
import {
  querySourceRuns,
  sourceRefKey,
  type SourceRef,
  type SourceRun,
  type SourceHistoryFilter,
} from "./sourceHistory";
import { SourceRunSummary } from "./SourceExecutionSummary";

export default function SourceExecutionHistory({
  source,
  onClose,
  onOpenRun,
  filter = {},
  inline = false,
  readOnly = false,
}: {
  source: SourceRef;
  onClose: () => void;
  onOpenRun?: (runId: number, respond?: boolean) => void;
  filter?: SourceHistoryFilter;
  inline?: boolean;
  readOnly?: boolean;
}) {
  const { t } = useTranslation();
  const filterJson = JSON.stringify(filter);
  const stableFilter = useMemo<SourceHistoryFilter>(() => JSON.parse(filterJson), [filterJson]);
  const key = sourceRefKey(source) + "|" + filterJson;
  const sourceJson = JSON.stringify(source);
  const stableSource = useMemo<SourceRef>(() => JSON.parse(sourceJson), [sourceJson]);
  const summaries = useMemo(() => new WeakMap<object, SourceRun>(), [stableSource]);
  const sharedDownloads = useMemo(() => new WeakMap<object, number[]>(), [stableSource]);
  const loadRuns = useCallback(
    async ({ pageIndex, pageSize }: { pageIndex: number; pageSize: number }) => {
      const [response] = await querySourceRuns([stableSource], pageIndex, pageSize, stableFilter);
      return {
        data:
          response?.runs.map((value) => {
            const run = {
              ...value.run,
              workflowName: value.workflowName ?? undefined,
              workflowIsBuiltin: value.workflowIsBuiltin,
            };
            summaries.set(run, value);
            sharedDownloads.set(
              run,
              response.runs
                .filter(
                  (other) =>
                    other.run.id !== value.run.id &&
                    other.downloadTaskIds?.some((id) => value.downloadTaskIds?.includes(id)),
                )
                .map((other) => other.run.id),
            );
            return run;
          }) ?? [],
        totalCount: response?.runCount ?? 0,
      };
    },
    [stableSource, stableFilter, summaries, sharedDownloads],
  );
  return (
    <WorkflowRunsDrawer
      inline={inline}
      readOnly={readOnly}
      isOpen
      runSourceKey={key}
      workflowName={t("acquire.history.title")}
      headerContent={
        <p className="text-xs leading-relaxed text-default-500">
          {t("acquire.history.attemptHint")}
        </p>
      }
      emptyContent={
        <div className="space-y-2 text-left">
          <p className="font-medium text-foreground">{t("acquire.history.notStarted")}</p>
          <p className="leading-relaxed">{t("acquire.history.notStartedHint")}</p>
        </div>
      }
      loadRuns={loadRuns}
      renderRunSummary={(run) => {
        const value = summaries.get(run);
        return value ? (
          <SourceRunSummary value={value} sharedRunIds={sharedDownloads.get(run)} />
        ) : null;
      }}
      onClose={onClose}
      onOpenRun={onOpenRun}
    />
  );
}
