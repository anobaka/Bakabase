import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, Select, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import { planTaskApi } from "./api";
import type { PlanBuilder, PlanTaskDetail } from "./models";

/** Title search is useful only while the task has no download sources. */
export default function DraftPreparation({ detail, busy, onAction }: {
  detail: PlanTaskDetail;
  busy: boolean;
  onAction: (action: () => Promise<PlanTaskDetail>) => Promise<void>;
}) {
  const { t } = useTranslation();
  const [title, setTitle] = useState(detail.query ?? detail.task.title ?? "");
  const [builders, setBuilders] = useState<PlanBuilder[]>([]);
  const [builderId, setBuilderId] = useState(detail.task.builderId ?? "");
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    void planTaskApi.builders().then(items => {
      if (!active) return;
      const manual = items.filter(builder => builder.entryPoints.includes("manual") && builder.inputKind === "title");
      setBuilders(manual);
      setBuilderId(value => manual.some(builder => builder.id === value) ? value : manual[0]?.id ?? "");
    }).catch(() => { if (active) setFailed(true); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [retry]);
  if (detail.draftSources?.length || detail.plan?.parts.some(part => part.sources.length)) return null;
  return <section className="space-y-3" aria-label={t("acquisition.plan.search")}>
    <p className="text-sm text-default-500">{t("acquisition.preparation.searchHint")}</p>
    <Input label={t("acquisition.plan.titleLabel")} value={title} onValueChange={setTitle} maxLength={512} isDisabled={busy} />
    {loading ? <Spinner size="sm" /> : failed ? <div role="alert" className="flex items-center gap-2 text-danger">
      {t("acquisition.plan.loadFailed")}
      <Button size="sm" variant="light" onPress={() => setRetry(value => value + 1)}>{t("common.action.retry")}</Button>
    </div> : <Select label={t("acquisition.plan.builderLabel")} selectedKeys={builderId ? [builderId] : []}
      isDisabled={busy} disallowEmptySelection
      dataSource={builders.map(builder => ({ value: builder.id, label: t(builder.nameKey), textValue: t(builder.nameKey), description: t(builder.descriptionKey) }))}
      onSelectionChange={keys => setBuilderId(String(Array.from(keys)[0] ?? ""))} />}
    <Button color="primary" variant="flat" isDisabled={busy || loading || failed || !builderId || !title.trim()}
      onPress={() => void onAction(async () => {
        if (detail.task.builderId !== builderId) {
          const saved = await planTaskApi.updateDraft(detail.task.id, { expectedRevision: detail.task.planRevision, title: title.trim() });
          await planTaskApi.setBuilder(saved.task.id, saved.task.planRevision, builderId);
        }
        return planTaskApi.search(detail.task.id, title.trim());
      })}>{t("acquisition.draft.search")}</Button>
    {detail.candidates?.length > 0 && <div className="space-y-2">
      <p className="text-xs text-default-500">{t("acquisition.plan.chooseHint", { count: detail.candidates.length })}</p>
      {detail.candidates.map(candidate => <div key={candidate.id} className="flex flex-wrap items-center justify-between gap-3 rounded-lg bg-default-50 p-3">
        <div className="min-w-0 flex-1">
          <p className="break-words text-sm font-medium">{candidate.title}</p>
          <ExternalLink href={candidate.url} className="text-xs">{t("acquisition.plan.candidateSource")}</ExternalLink>
        </div>
        <Button size="sm" variant="flat" color="primary" isDisabled={busy}
          onPress={() => void onAction(() => planTaskApi.select(detail.task.id, candidate.id, detail.task.planRevision))}>
          {t("acquisition.plan.choose")}
        </Button>
      </div>)}
    </div>}
    {detail.query != null && detail.candidates?.length === 0 && <p className="text-sm text-default-500">{t("acquisition.plan.noCandidates")}</p>}
  </section>;
}
