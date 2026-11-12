import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Chip } from "@/components/bakaui";
import StoragePolicyFields, {
  defaultStoragePolicy,
  resolveStoragePolicy,
  storagePolicyIsInvalid,
  type StoragePolicy,
  type StoragePolicyOverride,
} from "../StoragePolicyFields";
import type { PlanTaskDetail } from "./models";

export type StorageOverrideInput = {
  partId: string;
  stepId?: string;
  policy?: StoragePolicyOverride | null;
};
type Editor = { revision: number; policy: StoragePolicy; overrides: StorageOverrideInput[] };
const keyOf = (partId: string, stepId?: string) => JSON.stringify([partId, stepId ?? null]);

/** Policies are frozen with the plan. Editing keeps the original revision even while status polling continues. */
export default function TaskStoragePolicySettings({
  detail,
  busy = false,
  onSave,
}: {
  detail: PlanTaskDetail;
  busy?: boolean;
  onSave?: (
    revision: number,
    policy: StoragePolicy,
    overrides: StorageOverrideInput[],
  ) => Promise<boolean>;
}) {
  const { t } = useTranslation();
  const [editor, setEditor] = useState<Editor>();
  const editable =
    !!onSave &&
    detail.canEditStoragePolicy === true &&
    !detail.task.isDeleted &&
    !detail.task.deletedAt;
  const policy = detail.storagePolicy ?? detail.plan?.storagePolicy;
  const start = () =>
    setEditor({
      revision: detail.task.planRevision,
      policy: policy ?? defaultStoragePolicy,
      overrides: (detail.plan?.parts ?? []).flatMap((part) => [
        { partId: part.id, policy: part.storagePolicy ?? null },
        ...((
          part.processing ??
          part.sources.find((source) => source.id === part.selectedSourceId)?.processing
        )?.steps?.map((step) => ({
          partId: part.id,
          stepId: step.id,
          policy: step.storagePolicy ?? null,
        })) ?? []),
      ]),
    });
  const stale = !!editor && (editor.revision !== detail.task.planRevision || !editable);
  const override = (partId: string, stepId?: string) =>
    editor?.overrides.find((item) => keyOf(item.partId, item.stepId) === keyOf(partId, stepId))
      ?.policy;
  const changeOverride = (
    partId: string,
    stepId: string | undefined,
    next: StoragePolicyOverride | null,
  ) =>
    setEditor(
      (current) =>
        current && {
          ...current,
          overrides: [
            ...current.overrides.filter(
              (item) => keyOf(item.partId, item.stepId) !== keyOf(partId, stepId),
            ),
            { partId, stepId, policy: next },
          ],
        },
    );
  const invalid =
    !!editor &&
    (storagePolicyIsInvalid(editor.policy) ||
      editor.overrides.some((item) => {
        const partPolicy = resolveStoragePolicy(editor.policy, override(item.partId));
        return storagePolicyIsInvalid(resolveStoragePolicy(partPolicy, item.policy));
      }));
  const fields = (parent: StoragePolicy, partId: string, stepId?: string) => {
    const local = override(partId, stepId);
    return (
      <div className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Chip size="sm" variant="flat">
            {t(local ? "acquisition.storage.overridden" : "acquisition.storage.inherited")}
          </Chip>
          {!!local && (
            <Button
              size="sm"
              variant="light"
              isDisabled={busy || stale}
              onPress={() => changeOverride(partId, stepId, null)}
            >
              {t("acquisition.storage.resetOverride")}
            </Button>
          )}
        </div>
        <StoragePolicyFields
          value={resolveStoragePolicy(parent, local)}
          disabled={busy || stale}
          processingOnly={!!stepId}
          showHint={false}
          onChange={(next) => {
            const keys = stepId
              ? ([
                  "processingDirectoryTemplate",
                  "cleanupIntermediates",
                  "materialization",
                ] as const)
              : ([
                  "downloadDirectoryTemplate",
                  "processingDirectoryTemplate",
                  "processingWorkspaceDirectory",
                  "cleanupIntermediates",
                  "materialization",
                  "sourceCleanup",
                ] as const);
            const current = resolveStoragePolicy(parent, local);
            const changed = Object.fromEntries(
              keys.filter((key) => next[key] !== current[key]).map((key) => [key, next[key]]),
            );
            const updated = { ...local, ...changed };
            changeOverride(partId, stepId, Object.keys(updated).length ? updated : null);
          }}
        />
      </div>
    );
  };
  const summary = (value: StoragePolicy, processingOnly = false) => (
    <dl className="grid grid-cols-1 gap-1 text-xs sm:grid-cols-[auto_1fr] sm:gap-x-3">
      {!processingOnly && (
        <>
          <dt className="text-default-500">{t("acquisition.storage.downloadTemplate")}</dt>
          <dd className="break-all font-mono">{value.downloadDirectoryTemplate}</dd>
        </>
      )}
      <dt className="text-default-500">{t("acquisition.storage.processingTemplate")}</dt>
      <dd className="break-all font-mono">{value.processingDirectoryTemplate}</dd>
      <dt className="text-default-500">{t("acquisition.workspace.title")}</dt>
      <dd className="break-all">
        {value.processingWorkspaceDirectory || t("acquisition.workspace.useInputStorage")}
      </dd>
      <dt className="text-default-500">{t("acquisition.storage.cleanupIntermediates")}</dt>
      <dd>
        {t(
          value.cleanupIntermediates
            ? "acquisition.storage.enabled"
            : "acquisition.storage.disabled",
        )}
      </dd>
      <dt className="text-default-500">{t("workflow.storage.materialization.label")}</dt>
      <dd>{t(`workflow.storage.materialization.${value.materialization || "auto"}`)}</dd>
      {!processingOnly && (
        <>
          <dt className="text-default-500">{t("acquisition.storage.sourceCleanup")}</dt>
          <dd>
            {t(`acquisition.storage.sourceCleanup.${value.sourceCleanup}`, {
              defaultValue: value.sourceCleanup,
            })}
          </dd>
        </>
      )}
    </dl>
  );
  return (
    <section className="space-y-3 rounded-lg border border-default-200 p-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("acquisition.storage.taskTitle")}</h3>
        {editable && !editor && (
          <Button size="sm" variant="flat" isDisabled={busy} onPress={start}>
            {t("acquisition.storage.edit")}
          </Button>
        )}
      </div>
      {editor ? (
        <>
          {stale && (
            <div
              role="alert"
              className="flex flex-wrap items-center gap-2 text-sm text-warning-600"
            >
              <span>{t("acquisition.storage.stale")}</span>
              <Button size="sm" variant="flat" isDisabled={busy || !editable} onPress={start}>
                {t("acquisition.storage.reload")}
              </Button>
            </div>
          )}
          <StoragePolicyFields
            value={editor.policy}
            disabled={busy || stale}
            onChange={(next) => setEditor({ ...editor, policy: next })}
          />
          {!!detail.plan?.parts.length && (
            <Accordion variant="bordered">
              {detail.plan.parts.map((part) => {
                const steps =
                  (
                    part.processing ??
                    part.sources.find((source) => source.id === part.selectedSourceId)?.processing
                  )?.steps ?? [];
                const partPolicy = resolveStoragePolicy(editor.policy, override(part.id));
                return (
                  <AccordionItem
                    key={part.id}
                    title={t("acquisition.storage.part", { title: part.title || part.id })}
                  >
                    <div className="space-y-3">
                      {fields(editor.policy, part.id)}
                      {!!steps.length && (
                        <Accordion variant="bordered">
                          {steps.map((step) => (
                            <AccordionItem
                              key={step.id}
                              title={t("acquisition.storage.step", {
                                id: step.id,
                                kind: t(`acquisition.plan.processing.${step.op}`, {
                                  defaultValue: step.op,
                                }),
                              })}
                            >
                              {fields(partPolicy, part.id, step.id)}
                            </AccordionItem>
                          ))}
                        </Accordion>
                      )}
                    </div>
                  </AccordionItem>
                );
              })}
            </Accordion>
          )}
          <p className="text-xs text-default-500">{t("acquisition.storage.dynamicHint")}</p>
          <div className="flex flex-wrap gap-2">
            <Button
              size="sm"
              color="primary"
              isLoading={busy}
              isDisabled={busy || stale || invalid}
              onPress={async () => {
                if (await onSave?.(editor.revision, editor.policy, editor.overrides))
                  setEditor(undefined);
              }}
            >
              {t("acquisition.storage.save")}
            </Button>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() => setEditor(undefined)}
            >
              {t("acquisition.delivery.cancelEdit")}
            </Button>
          </div>
        </>
      ) : (
        <>
          <p className="text-xs text-default-500">
            {t(editable ? "acquisition.storage.taskHint" : "acquisition.storage.frozenHint")}
          </p>
          {policy ? (
            <>
              <div className="flex flex-wrap gap-2">
                <Chip size="sm" variant="flat">
                  {t("acquisition.storage.callerPreset")}
                </Chip>
                {!!detail.task.executionStartedAt && (
                  <Chip size="sm" variant="flat">
                    {t("acquisition.storage.snapshotFrozen")}
                  </Chip>
                )}
              </div>
              {summary(policy)}
              {!!detail.plan?.parts.length && (
                <Accordion variant="bordered">
                  {detail.plan.parts.map((part) => {
                    const partPolicy = resolveStoragePolicy(policy, part.storagePolicy);
                    const steps =
                      (
                        part.processing ??
                        part.sources.find((source) => source.id === part.selectedSourceId)
                          ?.processing
                      )?.steps ?? [];
                    return (
                      <AccordionItem
                        key={part.id}
                        title={t("acquisition.storage.part", { title: part.title || part.id })}
                      >
                        <div className="space-y-3">
                          <p className="text-xs text-default-500">
                            {t(
                              part.storagePolicy
                                ? "acquisition.storage.overridden"
                                : "acquisition.storage.inherited",
                            )}
                          </p>
                          {summary(partPolicy)}
                          {!!steps.length && (
                            <Accordion variant="bordered">
                              {steps.map((step) => (
                                <AccordionItem
                                  key={step.id}
                                  title={t("acquisition.storage.step", {
                                    id: step.id,
                                    kind: t(`acquisition.plan.processing.${step.op}`, {
                                      defaultValue: step.op,
                                    }),
                                  })}
                                >
                                  {summary(
                                    resolveStoragePolicy(partPolicy, step.storagePolicy),
                                    true,
                                  )}
                                </AccordionItem>
                              ))}
                            </Accordion>
                          )}
                        </div>
                      </AccordionItem>
                    );
                  })}
                </Accordion>
              )}
            </>
          ) : (
            <p className="text-xs text-default-500">{t("acquisition.storage.legacy")}</p>
          )}
        </>
      )}
    </section>
  );
}
