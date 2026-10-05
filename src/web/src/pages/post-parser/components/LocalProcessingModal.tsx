import type { DestroyableProps } from "@/components/bakaui/types";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { SelectItem } from "@heroui/react";

import { getDownloadInfo } from "../results";
import { getDownloadContentGroups } from "../resourceGroups";

import DownloadGroupHeader from "./DownloadGroupHeader";

import { Button, Checkbox, Input, Modal, Select, Switch } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { isProcessingPlanPayloadValid } from "@/components/Workflow/Triggers/FsProcessingPlan";

type Binding = {
  selected: boolean;
  directory: string;
  alreadyProcessed: boolean;
  runId?: number;
  error?: string;
};

export default function LocalProcessingModal({
  task,
  onDestroyed,
}: DestroyableProps & { task: PostParserTask }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const sections = getDownloadContentGroups(getDownloadInfo(task) ?? {});
  const resources = sections.flatMap((section) => section.resources);
  const bindingIndices = new Map(
    resources.map(({ sourceIndices }, index) => [sourceIndices[0], index]),
  );
  const [bindings, setBindings] = useState<Binding[]>(() =>
    resources.map(() => ({ selected: false, directory: "", alreadyProcessed: false })),
  );
  const [workflows, setWorkflows] = useState<{ id: number; name: string }[]>([]);
  const [workflowId, setWorkflowId] = useState<number>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [visible, setVisible] = useState(true);
  const update = (index: number, patch: Partial<Binding>) =>
    setBindings((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));

  useEffect(() => {
    let active = true;

    BApi.workflow
      .searchWorkflows({ triggerKind: "fs.processingPlan", enabledOnly: true })
      .then((response) => {
        if (response.code) throw new Error(response.message);
        if (!active) return;
        setWorkflows(response.data ?? []);
        setWorkflowId(response.data?.[0]?.id);
      })
      .catch((failure) => {
        if (active) setError(String(failure));
      });

    return () => {
      active = false;
    };
  }, []);
  const payload = (index: number) => ({
    directory: bindings[index].directory.trim(),
    extractionPlanJson: JSON.stringify(
      resources[index].resource.extraction ?? { requirement: "unknown", steps: [] },
    ),
    alreadyProcessed: bindings[index].alreadyProcessed,
    bindingId: `post:${task.id}:${task.revision ?? 0}:${resources[index].sourceIndices[0]}`,
    title: `${task.title || "Post"} · ${resources[index].sourceIndices[0] + 1}`,
  });
  const selected = bindings
    .map((row, index) => ({ row, index }))
    .filter(({ row }) => row.selected && row.runId == null);
  const ready =
    selected.length > 0 &&
    selected.every(({ index }) => isProcessingPlanPayloadValid(JSON.stringify(payload(index))));
  const start = async () => {
    if (busy || !workflowId || !ready) return;
    setBusy(true);
    setError(undefined);
    try {
      const directories = selected.map(({ row }) => row.directory.trim().replace(/[\\/]+$/, ""));

      if (new Set(directories).size !== directories.length)
        throw new Error(t("workflow.processing.distinctDirectories"));
      for (const { index } of selected) {
        try {
          const response = await BApi.workflow.runWorkflowManually(workflowId, {
            argsJson: JSON.stringify(payload(index)),
          });

          if (response.code || !response.data)
            throw new Error(response.message || t("postParser.result.failed"));
          update(index, { runId: response.data.id, error: undefined });
        } catch (failure) {
          update(index, { error: failure instanceof Error ? failure.message : String(failure) });
        }
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      footer={
        <div className="flex gap-2">
          <Button isDisabled={busy} onPress={() => setVisible(false)}>
            {t("postParser.action.close")}
          </Button>
          <Button
            color="primary"
            isDisabled={!workflowId || !ready || busy}
            isLoading={busy}
            onPress={start}
          >
            {t("workflow.processing.startSelected")}
          </Button>
        </div>
      }
      isDismissable={!busy}
      size="lg"
      title={t("postParser.action.processLocal")}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      <div className="space-y-4">
        <p className="text-sm">{t("workflow.processing.bindingHint")}</p>
        <p className="text-xs text-default-500">{t("postParser.groups.localProcessingHint")}</p>
        {error && (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        )}
        <Select
          isDisabled={busy}
          label={t("workflow.processing.workflow")}
          selectedKeys={workflowId ? [String(workflowId)] : []}
          onSelectionChange={(keys) => setWorkflowId(Number(Array.from(keys)[0]) || undefined)}
        >
          {workflows.map((workflow) => (
            <SelectItem key={workflow.id}>{workflow.name}</SelectItem>
          ))}
        </Select>
        {workflows.length === 0 && (
          <Button
            onPress={() => {
              setVisible(false);
              navigate("/workflows/editor?template=localProcessing");
            }}
          >
            {t("workflow.processing.configure")}
          </Button>
        )}
        {sections.map((section) => (
          <div
            key={section.key}
            aria-label={section.group?.title ?? t("postParser.groups.ungrouped")}
            className="space-y-2"
            role="group"
          >
            <DownloadGroupHeader group={section.group} resourceCount={section.resources.length} />
            {section.resources.map(({ resource, sourceIndices }) => {
              const index = bindingIndices.get(sourceIndices[0])!;

              return (
                <section
                  key={sourceIndices[0]}
                  className="space-y-2 rounded-lg border border-default-200 p-3"
                >
                  <Checkbox
                    isDisabled={busy || bindings[index].runId != null}
                    isSelected={bindings[index].selected}
                    onValueChange={(checked) => update(index, { selected: checked })}
                  >
                    <span className="break-all text-xs">
                      {resource.link || `${sourceIndices[0] + 1}`}
                    </span>
                  </Checkbox>
                  <Input
                    isDisabled={busy || bindings[index].runId != null}
                    label={t("workflow.processing.directory")}
                    size="sm"
                    value={bindings[index].directory}
                    onValueChange={(directory) => update(index, { directory })}
                  />
                  <Switch
                    isDisabled={busy || bindings[index].runId != null}
                    isSelected={bindings[index].alreadyProcessed}
                    size="sm"
                    onValueChange={(alreadyProcessed) => update(index, { alreadyProcessed })}
                  >
                    {t("workflow.processing.alreadyProcessed")}
                  </Switch>
                  {!bindings[index].alreadyProcessed && (
                    <p className="text-xs text-default-500">
                      {t(`postParser.extraction.${resource.extraction?.requirement ?? "unknown"}`)}
                    </p>
                  )}
                  {bindings[index].runId != null && (
                    <p className="text-xs text-success" role="status">
                      {t("postParser.label.run", { id: bindings[index].runId })}
                    </p>
                  )}
                  {bindings[index].error && (
                    <p className="text-xs text-danger" role="alert">
                      {bindings[index].error}
                    </p>
                  )}
                </section>
              );
            })}
          </div>
        ))}
      </div>
    </Modal>
  );
}
