import type { DestroyableProps } from "@/components/bakaui/types";
import type { PostParserTask } from "@/core/models/PostParserTask";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { SelectItem } from "@heroui/react";
import { AiOutlineLink } from "react-icons/ai";

import { getDownloadInfo } from "../results";
import { getDownloadContentGroups } from "../resourceGroups";

import DownloadGroupHeader from "./DownloadGroupHeader";

import {
  Accordion,
  AccordionItem,
  Button,
  Checkbox,
  Modal,
  Select,
  Switch,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { isProcessingPlanPayloadValid } from "@/components/Workflow/Triggers/FsProcessingPlan";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import WorkflowBrowserModal from "@/components/Workflow/WorkflowBrowserModal";
import WorkflowSummary from "@/components/Workflow/WorkflowSummary";
import LocalFileWorkflowPreview from "@/components/Workflow/LocalFileWorkflowPreview";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import ExternalLink from "@/components/ExternalLink";
import { ThirdPartyId } from "@/sdk/constants";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";

type Binding = {
  selected: boolean;
  directory: string;
  alreadyProcessed: boolean;
  runId?: number;
  error?: string;
};

export default function LocalProcessingModal({
  task,
  onCreated,
  onDestroyed,
}: DestroyableProps & { task: PostParserTask; onCreated?: () => void }) {
  const { t } = useTranslation();
  const info = getDownloadInfo(task);
  const sections = getDownloadContentGroups(info ?? {});
  const resources = sections.flatMap((section) => section.resources);
  const bindingIndices = new Map(
    resources.map(({ sourceIndices }, index) => [sourceIndices[0], index]),
  );
  const [bindings, setBindings] = useState<Binding[]>(() =>
    resources.map(() => ({ selected: false, directory: "", alreadyProcessed: false })),
  );
  const [workflows, setWorkflows] = useState<Definition[]>([]);
  const [workflowId, setWorkflowId] = useState<number>();
  const [error, setError] = useState<string>();
  const [workflowError, setWorkflowError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [visible, setVisible] = useState(true);
  const [browserOpen, setBrowserOpen] = useState(false);
  const [workflowLoading, setWorkflowLoading] = useState(true);
  const loadVersion = useRef(0);
  const update = (index: number, patch: Partial<Binding>) =>
    setBindings((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  const selectSource = (index: number, checked: boolean) => {
    const groupId = resources[index].resource.groupId;
    setBindings((current) =>
      current.map((row, i) =>
        i === index
          ? { ...row, selected: checked }
          : checked && groupId && resources[i].resource.groupId === groupId && row.runId == null
            ? { ...row, selected: false }
            : row,
      ),
    );
  };

  const loadWorkflows = useCallback(async () => {
    const version = ++loadVersion.current;
    setWorkflowLoading(true);
    try {
      const response = await BApi.workflow.searchWorkflows({
        triggerKind: "fs.processingPlan",
        enabledOnly: true,
      });
      if (version !== loadVersion.current) return;
      if (response.code) throw new Error(response.message);
      const next = response.data ?? [];
      setWorkflowError(undefined);
      setWorkflows(next);
      setWorkflowId((current) =>
        next.some((workflow) => workflow.id === current) ? current : next[0]?.id,
      );
    } catch (failure) {
      if (version === loadVersion.current) setWorkflowError(String(failure));
    } finally {
      if (version === loadVersion.current) setWorkflowLoading(false);
    }
  }, []);
  useEffect(() => {
    void loadWorkflows();
    return () => {
      loadVersion.current++;
    };
  }, [loadWorkflows]);
  const selectedWorkflow = workflows.find((workflow) => workflow.id === workflowId);
  const sourceTitle = (index: number) => {
    const entry = resources[index];
    const group = sections.find((section) => section.resources.includes(entry))?.group;
    return typeof entry.resource.title === "string" && entry.resource.title.trim()
      ? entry.resource.title
      : group?.title ||
          info?.title ||
          task.title ||
          t("workflow.processing.binding.sourceTitle", { number: entry.sourceIndices[0] + 1 });
  };
  const payload = (index: number) => ({
    directory: bindings[index].directory.trim(),
    extractionPlanJson: JSON.stringify(
      resources[index].resource.extraction ?? { requirement: "unknown", steps: [] },
    ),
    alreadyProcessed: bindings[index].alreadyProcessed,
  });
  const selected = bindings
    .map((row, index) => ({ row, index }))
    .filter(({ row }) => row.selected && row.runId == null);
  const ready =
    !workflowLoading &&
    !!selectedWorkflow &&
    selected.length > 0 &&
    selected.every(({ index }) => isProcessingPlanPayloadValid(JSON.stringify(payload(index))));
  const start = async () => {
    if (busyRef.current || !workflowId || !ready) return;
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    let created = false;
    try {
      const directories = selected.map(({ row }) => row.directory.trim().replace(/[\\/]+$/, ""));

      if (new Set(directories).size !== directories.length)
        throw new Error(t("workflow.processing.distinctDirectories"));
      for (const { index } of selected) {
        try {
          const response = await BApi.downloadIntents.startPostLocalProcessing(
            {
              taskId: task.id,
              revision: task.revision ?? 0,
              resourceIndex: resources[index].sourceIndices[0],
              workflowDefinitionId: workflowId,
              files: [],
              alreadyProcessed: bindings[index].alreadyProcessed,
            },
            { directory: bindings[index].directory.trim() },
          );

          if (response.code || !response.data)
            throw new Error(response.message || t("postParser.result.failed"));
          update(index, { runId: response.data.id, error: undefined });
          created = true;
        } catch (failure) {
          update(index, { error: failure instanceof Error ? failure.message : String(failure) });
        }
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      busyRef.current = false;
      setBusy(false);
      if (created) onCreated?.();
    }
  };

  return (
    <>
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
        isDismissable={!busy && !browserOpen}
        size="5xl"
        title={t("postParser.action.processLocal")}
        visible={visible}
        onClose={() => setVisible(false)}
        onDestroyed={onDestroyed}
      >
        <div className="space-y-4">
          <p className="text-sm">{t("workflow.processing.binding.introduction")}</p>
          <section className="space-y-2" aria-label={t("workflow.processing.binding.title")}>
            <h3 className="font-medium">{t("workflow.processing.binding.title")}</h3>
            <p className="text-xs text-default-500">
              {t("workflow.processing.binding.groupRules")}
            </p>
            <p className="text-xs text-default-500">
              {t("workflow.processing.binding.folderOnly")}
            </p>
          </section>
          {error && (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          )}
          {workflowError && (
            <p className="text-sm text-danger" role="alert">
              {workflowError}
            </p>
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
                      aria-label={resource.link || String(sourceIndices[0] + 1)}
                      isDisabled={busy || bindings[index].runId != null}
                      isSelected={bindings[index].selected}
                      onValueChange={(checked) => selectSource(index, checked)}
                    >
                      <span className="break-words text-sm font-medium">{sourceTitle(index)}</span>
                      <span className="sr-only"> {resource.link}</span>
                    </Checkbox>
                    <div className="flex min-w-0 items-center gap-2 text-xs">
                      <SourceSiteIcon link={resource.link} />
                      {webLink(resource.link) ? (
                        <ExternalLink href={resource.link!} className="min-w-0 break-all" size="sm">
                          {resource.link}
                        </ExternalLink>
                      ) : (
                        <span className="break-all text-default-500">{resource.link}</span>
                      )}
                    </div>
                    <FileSystemSelectorButton
                      key={`${index}:${bindings[index].directory}:${busy}:${bindings[index].runId ?? "draft"}`}
                      className="h-auto min-h-8 min-w-0 max-w-full justify-start whitespace-normal break-all text-left"
                      title={bindings[index].directory || undefined}
                      aria-label={`${t("workflow.processing.chooseDirectory")}: ${sourceTitle(index)}`}
                      isDisabled={busy || bindings[index].runId != null}
                      fileSystemSelectorProps={{
                        targetType: "folder",
                        multiple: false,
                        startPath: bindings[index].directory || undefined,
                        defaultSelectedPath: bindings[index].directory || undefined,
                        onSelected: (entry) => {
                          if (!entry.path || busyRef.current) return;
                          setBindings((current) =>
                            current.map((row, i) =>
                              i === index && row.runId == null
                                ? { ...row, directory: entry.path }
                                : row,
                            ),
                          );
                        },
                      }}
                    >
                      {t("workflow.processing.chooseDirectory")}
                    </FileSystemSelectorButton>
                    <div className="block">
                      <Switch
                        isDisabled={busy || bindings[index].runId != null}
                        isSelected={bindings[index].alreadyProcessed}
                        size="sm"
                        onValueChange={(alreadyProcessed) => update(index, { alreadyProcessed })}
                      >
                        {t("workflow.processing.alreadyProcessed")}
                      </Switch>
                    </div>
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
          <section
            className="space-y-3 border-t border-default-200 pt-4"
            aria-label={t("workflow.processing.related.selectionTitle")}
          >
            <h3 className="font-medium">{t("workflow.processing.related.selectionTitle")}</h3>
            <Select
              isDisabled={busy || workflowLoading}
              label={t("workflow.processing.workflow")}
              description={t("workflow.processing.workflowHint")}
              selectedKeys={workflowId ? [String(workflowId)] : []}
              onSelectionChange={(keys) => setWorkflowId(Number(Array.from(keys)[0]) || undefined)}
            >
              {workflows.map((workflow) => (
                <SelectItem key={workflow.id} textValue={workflowLabel(workflow, t)}>
                  {workflowLabel(workflow, t)}
                </SelectItem>
              ))}
            </Select>
            <Button
              isDisabled={busy}
              size="sm"
              variant="light"
              onPress={() => setBrowserOpen(true)}
            >
              {t("workflow.processing.viewWorkflows")}
            </Button>
            {!workflows.length && !workflowLoading && (
              <p className="text-sm text-warning-600">{t("workflow.processing.related.empty")}</p>
            )}
            {selectedWorkflow && (
              <WorkflowSummary
                workflow={selectedWorkflow}
                activityKinds={[...(selectedWorkflow.activities ?? [])]
                  .sort((a, b) => a.order - b.order)
                  .map((activity) => activity.kind)}
                showDiagnostics={false}
              />
            )}
            <p className="text-xs text-default-500">{t("workflow.processing.preview.boundary")}</p>
            <p className="text-xs text-default-500">
              {t("workflow.processing.preview.retentionBoundary")}
            </p>
            {!selected.length && (
              <p className="text-xs text-default-500">
                {t("workflow.processing.preview.selectSource")}
              </p>
            )}
            {selectedWorkflow && selected.length > 0 && (
              <Accordion
                key={`${workflowId}:${selected.map(({ index }) => index).join(",")}`}
                selectionMode="multiple"
                defaultExpandedKeys="all"
                variant="splitted"
              >
                {selected.map(({ index }) => (
                  <AccordionItem key={String(index)} title={sourceTitle(index)}>
                    <LocalFileWorkflowPreview
                      workflow={selectedWorkflow}
                      instructions={resources[index].resource.extraction}
                      alreadyProcessed={bindings[index].alreadyProcessed}
                    />
                  </AccordionItem>
                ))}
              </Accordion>
            )}
          </section>
        </div>
      </Modal>
      {browserOpen && (
        <WorkflowBrowserModal
          visible
          onClose={() => setBrowserOpen(false)}
          triggerKind="fs.processingPlan"
          onChanged={() => void loadWorkflows()}
        />
      )}
    </>
  );
}

function webLink(link?: string): boolean {
  try {
    return ["http:", "https:"].includes(new URL(link ?? "").protocol);
  } catch {
    return false;
  }
}

function SourceSiteIcon({ link }: { link?: string }) {
  let host = "";
  try {
    host = new URL(link ?? "").hostname.toLowerCase();
  } catch {
    /* A legacy source can remain plain text. */
  }
  const site = ["pan.baidu.com", "yun.baidu.com"].includes(host)
    ? ThirdPartyId.BaiduNetdisk
    : ["drive.google.com", "docs.google.com"].includes(host)
      ? ThirdPartyId.GoogleDrive
      : ["gofile.io", "www.gofile.io"].includes(host)
        ? ThirdPartyId.Gofile
        : ["e-hentai.org", "exhentai.org"].includes(host)
          ? ThirdPartyId.ExHentai
          : undefined;
  return site == null ? (
    <AiOutlineLink aria-hidden className="shrink-0 text-base text-default-500" />
  ) : (
    <span aria-hidden className="shrink-0">
      <ThirdPartyIcon thirdPartyId={site} size="sm" />
    </span>
  );
}
