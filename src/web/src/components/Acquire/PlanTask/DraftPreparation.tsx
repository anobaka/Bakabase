import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Input, Select, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import AcquireModal from "../AcquireModal";
import { defaultAcquireAdapter } from "../api";
import { planTaskApi } from "./api";
import type { DraftSource, PlanBuilder, PlanTaskDetail } from "./models";
import { draftPlanFromRequest } from "./draftPlan";

export default function DraftPreparation({
  detail,
  busy,
  onAction,
}: {
  detail: PlanTaskDetail;
  busy: boolean;
  onAction: (action: () => Promise<PlanTaskDetail>) => Promise<void>;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [title, setTitle] = useState(detail.task.title ?? "");
  const [sources, setSources] = useState<DraftSource[]>(detail.draftSources ?? []);
  const [builders, setBuilders] = useState<PlanBuilder[]>([]);
  const [builderId, setBuilderId] = useState("");
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    setTitle(detail.task.title ?? "");
    setSources(detail.draftSources ?? []);
  }, [detail.task.id, detail.task.planRevision]);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    void planTaskApi
      .builders()
      .then((items) => {
        if (!active) return;
        const manual = items.filter(
          (builder) => builder.entryPoints.includes("manual") && builder.inputKind === "title",
        );
        setBuilders(manual);
        setBuilderId((value) =>
          manual.some((builder) => builder.id === value) ? value : (manual[0]?.id ?? ""),
        );
      })
      .catch(() => {
        if (active) setFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [retry]);
  const patch = (id: string, fields: Partial<DraftSource>) =>
    setSources((items) => items.map((item) => (item.id === id ? { ...item, ...fields } : item)));
  const save = () =>
    planTaskApi.updateDraft(detail.task.id, {
      expectedRevision: detail.task.planRevision,
      title: title.trim() || undefined,
      sources: sources
        .filter((source) => source.url.trim())
        .map((source) => ({ ...source, url: source.url.trim() })),
    });
  const hasInput = !!title.trim() || sources.some((source) => source.url.trim());
  return (
    <section
      className="space-y-4 rounded-lg border border-primary-200 p-3"
      aria-label={t("acquisition.draft.complete")}
    >
      <p className="text-sm text-default-500">{t("acquisition.draft.completeHint")}</p>
      <Input
        label={t("acquisition.plan.titleLabel")}
        value={title}
        onValueChange={setTitle}
        maxLength={512}
        isDisabled={busy}
      />
      <div className="space-y-3">
        {sources.map((source, index) => (
          <div key={source.id} className="space-y-2 rounded-lg bg-default-50 p-3">
            <Input
              label={t("acquisition.draft.source", { number: index + 1 })}
              value={source.url}
              onValueChange={(url) => patch(source.id, { url })}
              isDisabled={busy}
            />
            <Accordion isCompact className="px-0">
              <AccordionItem key="credentials" title={t("acquisition.draft.accessDetails")}>
                <div className="space-y-2">
                  <Input
                    label={t("acquisition.draft.accessCode")}
                    value={source.accessCode ?? ""}
                    onValueChange={(accessCode) => patch(source.id, { accessCode })}
                    isDisabled={busy}
                  />
                  <ArchivePasswordInput
                    label={t("acquisition.sharedList.column.password")}
                    value={source.archivePassword ?? ""}
                    onValueChange={(archivePassword) => patch(source.id, { archivePassword })}
                    isDisabled={busy}
                  />
                </div>
              </AccordionItem>
            </Accordion>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() => setSources((items) => items.filter((item) => item.id !== source.id))}
            >
              {t("acquisition.draft.removeSource")}
            </Button>
          </div>
        ))}
        <Button
          size="sm"
          variant="flat"
          isDisabled={busy}
          onPress={() =>
            setSources((items) => [...items, { id: crypto.randomUUID(), url: "", kind: "auto" }])
          }
        >
          {t("acquisition.draft.addSource")}
        </Button>
      </div>
      <Button variant="flat" isDisabled={busy || !hasInput} onPress={() => void onAction(save)}>
        {t("acquisition.draft.save")}
      </Button>
      <div className="space-y-3 border-t border-default-200 pt-3">
        <p className="text-sm font-medium">{t("acquisition.draft.build")}</p>
        <p className="text-xs text-default-500">{t("acquisition.draft.buildHint")}</p>
        {loading ? (
          <Spinner size="sm" />
        ) : failed ? (
          <div role="alert" className="flex flex-wrap items-center gap-2 text-sm text-danger">
            {t("acquisition.plan.loadFailed")}
            <Button size="sm" variant="light" onPress={() => setRetry((value) => value + 1)}>
              {t("common.action.retry")}
            </Button>
          </div>
        ) : (
          <Select
            label={t("acquisition.plan.builderLabel")}
            selectedKeys={builderId ? [builderId] : []}
            isDisabled={busy}
            disallowEmptySelection
            dataSource={builders.map((builder) => ({
              value: builder.id,
              label: t(builder.nameKey),
              textValue: t(builder.nameKey),
              description: t(builder.descriptionKey),
            }))}
            onSelectionChange={(keys) => setBuilderId(String(Array.from(keys)[0] ?? ""))}
          />
        )}
        <div className="flex flex-wrap gap-2">
          <Button
            color="primary"
            variant="flat"
            isDisabled={busy || loading || failed || !builderId || !title.trim()}
            onPress={() =>
              void onAction(async () => {
                const saved = await save();
                await planTaskApi.setBuilder(saved.task.id, saved.task.planRevision, builderId);
                return planTaskApi.search(saved.task.id, title.trim());
              })
            }
          >
            {t("acquisition.draft.search")}
          </Button>
          <Button
            color="primary"
            variant="flat"
            isDisabled={busy || !sources.some((source) => source.url.trim())}
            onPress={() =>
              void onAction(async () => {
                const saved = await save();
                createPortal(AcquireModal, {
                  purpose: "plan",
                  title: saved.task.title ?? title,
                  initialOutputDirectory: saved.task.targetDirectory,
                  planLabels: {
                    title: t("acquisition.draft.directTitle"),
                    confirm: t("acquisition.draft.prepare"),
                    completed: t("acquisition.draft.prepared"),
                  },
                  candidates: (saved.draftSources ?? []).map((source) => ({
                    ...source,
                    extractionPlanJson: source.processing
                      ? JSON.stringify(source.processing)
                      : undefined,
                  })),
                  adapter: {
                    preflight: defaultAcquireAdapter.preflight,
                    submit: async (request, directory) => {
                      const next = await planTaskApi.prepare(
                        saved.task.id,
                        saved.task.planRevision,
                        draftPlanFromRequest(request, directory, saved.task.title ?? title),
                      );
                      await onAction(async () => next);
                      return {
                        mode: "files",
                        runs: [],
                        resourceIds: [],
                        acquisitionTaskIds: [saved.task.id],
                      };
                    },
                  },
                });
                return saved;
              })
            }
          >
            {t("acquisition.draft.direct")}
          </Button>
        </div>
      </div>
    </section>
  );
}
