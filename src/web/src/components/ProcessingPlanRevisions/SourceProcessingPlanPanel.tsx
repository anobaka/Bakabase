import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Chip, Spinner } from "@/components/bakaui";
import type { BakabaseModulesWorkflowArtifactsFileProcessingPlan as ProcessingPlan } from "@/sdk/Api";
import type { SourceRef } from "@/components/Acquire/sourceHistory";
import { querySourceProcessingPlan, type SourceProcessingPlanView } from "./api";
import PlanSummary from "./PlanSummary";

export default function SourceProcessingPlanPanel({
  source,
  currentPlan,
  appliedRevisionId,
  frozen = false,
  isDisabled = false,
  deferUntilVisible = false,
  onApply,
}: {
  source: SourceRef;
  currentPlan?: ProcessingPlan | null;
  appliedRevisionId?: string | null;
  frozen?: boolean;
  isDisabled?: boolean;
  /** Large paginated lists can defer source history until this panel approaches the viewport. */
  deferUntilVisible?: boolean;
  onApply?: (revisionId: string) => Promise<void>;
}) {
  const { t } = useTranslation();
  const sourceJson = JSON.stringify(source);
  const stableSource = useMemo(() => JSON.parse(sourceJson) as SourceRef, [sourceJson]);
  const [view, setView] = useState<SourceProcessingPlanView>();
  const [failed, setFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState(false);
  const [applying, setApplying] = useState(false);
  const [applyFailed, setApplyFailed] = useState(false);
  const section = useRef<HTMLElement>(null);
  const [shouldLoad, setShouldLoad] = useState(
    () => !deferUntilVisible || typeof IntersectionObserver === "undefined",
  );
  const requestId = useRef(0);
  const load = useCallback(async () => {
    const request = ++requestId.current;
    setLoading(true);
    setFailed(false);
    try {
      const next = await querySourceProcessingPlan(stableSource);
      if (request === requestId.current) setView(next);
    } catch {
      if (request === requestId.current) setFailed(true);
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  }, [stableSource]);
  useEffect(() => {
    if (shouldLoad) return;
    if (!deferUntilVisible || typeof IntersectionObserver === "undefined") {
      setShouldLoad(true);
      return;
    }
    let active = true;
    const observer = new IntersectionObserver(
      (entries) => {
        if (!active || !entries.some((entry) => entry.isIntersecting)) return;
        setShouldLoad(true);
        observer.disconnect();
      },
      { rootMargin: "600px" },
    );
    if (section.current) observer.observe(section.current);
    return () => {
      active = false;
      observer.disconnect();
    };
  }, [deferUntilVisible, shouldLoad]);
  useEffect(() => {
    setView(undefined);
    setFailed(false);
    if (shouldLoad) void load();
    return () => {
      requestId.current++;
    };
  }, [load, shouldLoad]);
  const effective =
    view?.sourceAvailable === false
      ? undefined
      : view?.history.find(
          (entry) =>
            entry.revision === view.effectiveRevision && entry.sourceHash === view.sourceHash,
        );
  const alreadyApplied = !!effective && effective.id === appliedRevisionId;
  return (
    <section
      ref={section}
      className="space-y-2 rounded-lg border border-default-200 p-2"
      aria-label={t("processingRevision.title")}
    >
      <div className="flex flex-wrap items-center gap-2">
        {effective && (
          <Chip size="sm" variant="flat" color="primary">
            {t("processingRevision.revised")}
          </Chip>
        )}
        {alreadyApplied && (
          <Chip size="sm" variant="flat" color="success">
            {t("processingRevision.applied")}
          </Chip>
        )}
        <Button
          size="sm"
          variant="light"
          onPress={() => {
            setShouldLoad(true);
            setOpen((value) => !value);
          }}
          aria-expanded={open}
        >
          {t(open ? "processingRevision.hide" : "processingRevision.inspect")}
        </Button>
        {shouldLoad && loading && (
          <Spinner size="sm" aria-label={t("processingRevision.loading")} />
        )}
      </div>
      {failed && (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-xs text-danger">
          <span>{t("processingRevision.loadFailed")}</span>
          <Button size="sm" variant="light" onPress={() => void load()}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      {open && view && (
        <div className="space-y-3 text-sm">
          <p className="text-xs text-default-500">{t("processingRevision.originalRetained")}</p>
          {view.sourceAvailable === false && (
            <p role="status" className="text-xs text-warning-600">
              {t(`processingRevision.problem.${view.sourceProblem}`, {
                defaultValue: t("processingRevision.sourceUnavailable"),
              })}
            </p>
          )}
          {view.hasStaleRevisions && (
            <p className="text-xs text-warning-600">{t("processingRevision.stale")}</p>
          )}
          <div className="grid gap-3 md:grid-cols-2">
            <div className="space-y-2 rounded-lg bg-default-50 p-3">
              <h4 className="font-medium">{t("processingRevision.original")}</h4>
              <PlanSummary plan={view.originalPlan} />
            </div>
            <div className="space-y-2 rounded-lg bg-default-50 p-3">
              <h4 className="font-medium">{t("processingRevision.effective")}</h4>
              {view.sourceAvailable === false ? (
                <p className="text-xs text-default-500">
                  {t("processingRevision.noEffectiveSource")}
                </p>
              ) : (
                <>
                  <PlanSummary plan={view.effectivePlan} />
                  <p className="text-xs text-default-500">
                    {t("processingRevision.effectiveHint")}
                  </p>
                </>
              )}
              {effective && (
                <div className="space-y-1 border-t border-default-200 pt-2 text-xs">
                  <p>
                    {t("processingRevision.version", { revision: effective.revision })} ·{" "}
                    <time dateTime={effective.createdAt}>
                      {new Date(effective.createdAt).toLocaleString()}
                    </time>
                  </p>
                  <p className="whitespace-pre-wrap break-words">{effective.reason}</p>
                  <p className="font-medium">{t("processingRevision.evidence")}</p>
                  {effective.evidence.map((evidence, index) => (
                    <blockquote
                      key={index}
                      className="whitespace-pre-wrap break-words border-l-2 border-default-300 pl-2"
                    >
                      {evidence.quote}
                    </blockquote>
                  ))}
                </div>
              )}
            </div>
          </div>
          {currentPlan !== undefined && (
            <div className="space-y-2 rounded-lg border border-default-200 p-3">
              <h4 className="font-medium">
                {t(frozen ? "processingRevision.frozenTask" : "processingRevision.currentTask")}
              </h4>
              <PlanSummary plan={currentPlan} />
              <p className="text-xs text-default-500">
                {t(
                  frozen
                    ? "processingRevision.frozenHint"
                    : alreadyApplied
                      ? "processingRevision.appliedHint"
                      : "processingRevision.applyHint",
                )}
              </p>
            </div>
          )}
          {!frozen && onApply && effective && !alreadyApplied && (
            <Button
              size="sm"
              color="primary"
              variant="flat"
              isDisabled={isDisabled || loading || failed}
              isLoading={applying}
              onPress={() => {
                setApplying(true);
                setApplyFailed(false);
                void onApply(effective.id)
                  .catch(() => setApplyFailed(true))
                  .finally(() => setApplying(false));
              }}
            >
              {t("processingRevision.apply")}
            </Button>
          )}
          {applyFailed && (
            <p role="alert" className="text-xs text-danger">
              {t("acquisition.plan.actionFailed")}
            </p>
          )}
          <Accordion isCompact className="px-0">
            <AccordionItem
              key="history"
              title={t("processingRevision.history", { count: view.history.length })}
            >
              {view.history.length ? (
                <ol className="space-y-3">
                  {[...view.history].reverse().map((entry) => (
                    <li key={entry.id} className="space-y-2 rounded-lg bg-default-50 p-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <span>{t("processingRevision.version", { revision: entry.revision })}</span>
                        <time dateTime={entry.createdAt} className="text-xs text-default-500">
                          {new Date(entry.createdAt).toLocaleString()}
                        </time>
                        {entry.sourceHash !== view.sourceHash && (
                          <Chip size="sm" variant="flat">
                            {t(
                              view.sourceAvailable === false
                                ? "processingRevision.retainedHistory"
                                : "processingRevision.outdated",
                            )}
                          </Chip>
                        )}
                      </div>
                      <p className="whitespace-pre-wrap break-words">{entry.reason}</p>
                      <PlanSummary plan={entry.plan} />
                      <div className="space-y-1 text-xs">
                        <p className="font-medium">{t("processingRevision.evidence")}</p>
                        {entry.evidence.map((evidence, index) => (
                          <blockquote
                            key={index}
                            className="whitespace-pre-wrap break-words border-l-2 border-default-300 pl-2"
                          >
                            {evidence.stepId && (
                              <span className="mr-2 text-default-500">{evidence.stepId}</span>
                            )}
                            {evidence.quote}
                          </blockquote>
                        ))}
                      </div>
                    </li>
                  ))}
                </ol>
              ) : (
                <p className="text-xs text-default-500">{t("processingRevision.noHistory")}</p>
              )}
            </AccordionItem>
          </Accordion>
          <Button size="sm" variant="light" isLoading={loading} onPress={() => void load()}>
            {t("processingRevision.refresh")}
          </Button>
        </div>
      )}
    </section>
  );
}
