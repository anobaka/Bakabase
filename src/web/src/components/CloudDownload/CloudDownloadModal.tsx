import type { DestroyableProps } from "@/components/bakaui/types";
import type { PostParserTask } from "@/core/models/PostParserTask";
import { lazy, Suspense, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import AccountSelect from "./AccountSelect";
import { Button, Checkbox, Input, Modal, Spinner } from "@/components/bakaui";
import { Select, SelectItem } from "@heroui/react";
import BApi from "@/sdk/BApi";
import { getDownloadInfo } from "@/pages/post-parser/results";
import { getDownloadContentGroups } from "@/pages/post-parser/resourceGroups";
import { getDownloadUrl } from "@/pages/post-parser/downloadLinks";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Definition } from "@/sdk/Api";

const LazyRunDetail = lazy(() => import("@/components/Workflow/WorkflowRunDetailModal"));
function WorkflowRunDetailModal(props: DestroyableProps & { runId: number }) {
  return (
    <Suspense fallback={<Spinner />}>
      <LazyRunDetail {...props} />
    </Suspense>
  );
}

export default function CloudDownloadModal({
  task,
  onCreated,
  onDestroyed,
}: DestroyableProps & { task?: PostParserTask; onCreated?: () => void }) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const defaultDirectory = useDownloaderGlobalOptionsStore(
    (state) => state.data?.defaultDownloadPath,
  );
  const [outputDirectory, setOutputDirectory] = useState(defaultDirectory ?? "");
  const [url, setUrl] = useState("");
  const [inputKind, setInputKind] = useState("share");
  const [accessCode, setAccessCode] = useState("");
  const [accountId, setAccountId] = useState<string>();
  const [workflowId, setWorkflowId] = useState<string>();
  const [workflows, setWorkflows] = useState<Definition[]>([]);
  const [error, setError] = useState<string>();
  const [runId, setRunId] = useState<number>();
  const [saving, setSaving] = useState(false);
  const [visible, setVisible] = useState(true);
  const sections = task
    ? getDownloadContentGroups(getDownloadInfo(task) ?? {})
        .map((section) => ({
          ...section,
          resources: section.resources.filter(
            ({ resource }) => !!getDownloadUrl(resource.link, resource.code),
          ),
        }))
        .filter((section) => section.resources.length > 0)
    : [];
  // The user explicitly chooses content; ungrouped legacy links have no safe mirror default.
  const [selected, setSelected] = useState<number[]>([]);
  useEffect(() => {
    void BApi.workflow
      .searchWorkflows({ triggerKind: "download.sources", enabledOnly: true })
      .then((response) => {
        setWorkflows(response.data ?? []);
      })
      .catch(() => setError(t("cloudDownload.workflow.loadFailed")));
  }, []);
  const valid =
    outputDirectory.trim().length > 0 &&
    (task
      ? selected.length > 0
      : inputKind === "remote"
        ? url.trim().startsWith("/")
        : /^https?:\/\//i.test(url.trim()) ||
          (inputKind === "share" && /^magnet:\?/i.test(url.trim())));
  const submit = async () => {
    if (!valid || saving) return;
    setSaving(true);
    setError(undefined);
    try {
      const common = {
        accountId,
        outputDirectory: outputDirectory.trim(),
        workflowDefinitionId: workflowId ? Number(workflowId) : undefined,
      };
      const response = task
        ? await BApi.cloudDownloads.startCloudDownloadFromPost(
            {
              ...common,
              taskId: task.id,
              revision: task.revision ?? 0,
              resourceIndices: [...new Set(selected)].sort((a, b) => a - b),
            },
            { outputDirectory: outputDirectory.trim() },
          )
        : await BApi.cloudDownloads.startCloudDownload(
            {
              ...common,
              sources: [
                {
                  id: "source-1",
                  url: url.trim(),
                  kind: inputKind,
                  providerId: inputKind === "remote" ? "baidu" : "unknown",
                  accessCode: accessCode.trim() || undefined,
                },
              ],
            },
            { outputDirectory: outputDirectory.trim() },
          );
      if (response.code || !response.data)
        throw new Error(response.message ?? t("cloudDownload.error"));
      setRunId(response.data.id);
      onCreated?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : t("cloudDownload.error"));
    } finally {
      setSaving(false);
    }
  };
  return (
    <Modal
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
      size="3xl"
      title={t("cloudDownload.title")}
      footer={
        <div className="flex justify-end gap-2">
          <Button variant="light" isDisabled={saving} onPress={() => setVisible(false)}>
            {t("common.action.close")}
          </Button>
          {runId ? (
            <Button color="primary" onPress={() => createPortal(WorkflowRunDetailModal, { runId })}>
              {t("cloudDownload.viewRun")}
            </Button>
          ) : (
            <Button
              color="primary"
              isLoading={saving}
              isDisabled={!valid}
              onPress={() => void submit()}
            >
              {t("cloudDownload.start")}
            </Button>
          )}
        </div>
      }
    >
      {runId ? (
        <p role="status" className="text-sm text-success">
          {t("cloudDownload.started", { id: runId })}
        </p>
      ) : (
        <div className="space-y-4">
          <p className="text-sm text-default-500">{t("cloudDownload.description")}</p>
          {task ? (
            <div className="max-h-64 space-y-3 overflow-auto">
              <p className="text-xs text-warning-600">{t("cloudDownload.chooseHint")}</p>
              {sections.map((section) => (
                <div key={section.key} className="space-y-2 rounded-lg bg-default-50 p-3">
                  <p className="text-sm font-medium">
                    {section.group?.title ?? t("cloudDownload.ungrouped")}
                  </p>
                  {section.resources.map(({ resource, sourceIndices }) => (
                    <Checkbox
                      key={sourceIndices[0]}
                      isSelected={selected.includes(sourceIndices[0])}
                      onValueChange={(checked) =>
                        setSelected((previous) => {
                          const siblings = section.group
                            ? new Set(section.resources.flatMap((item) => item.sourceIndices))
                            : new Set(sourceIndices);
                          return checked
                            ? [
                                ...previous.filter((index) => !siblings.has(index)),
                                sourceIndices[0],
                              ]
                            : previous.filter((index) => !sourceIndices.includes(index));
                        })
                      }
                    >
                      <span className="break-all text-xs">
                        {getDownloadUrl(resource.link, resource.code)}
                      </span>
                    </Checkbox>
                  ))}
                </div>
              ))}
            </div>
          ) : (
            <div className="space-y-3">
              <Select
                label={t("cloudDownload.inputKind")}
                selectedKeys={[inputKind]}
                disallowEmptySelection
                onSelectionChange={(keys) => {
                  setInputKind(String(Array.from(keys)[0]));
                  setUrl("");
                }}
              >
                <SelectItem key="share">{t("cloudDownload.share")}</SelectItem>
                <SelectItem key="remote">{t("cloudDownload.remote")}</SelectItem>
                <SelectItem key="http">{t("cloudDownload.http")}</SelectItem>
              </Select>
              <Input
                label={t(inputKind === "remote" ? "cloudDownload.remotePath" : "cloudDownload.url")}
                placeholder={inputKind === "remote" ? "/我的文件/目录" : "https://…"}
                value={url}
                onValueChange={setUrl}
              />
              {inputKind === "share" && (
                <Input
                  label={t("cloudDownload.accessCode")}
                  value={accessCode}
                  onValueChange={setAccessCode}
                />
              )}
            </div>
          )}
          <AccountSelect value={accountId} onChange={setAccountId} />
          <DownloadDirectoryField
            value={outputDirectory}
            onChange={setOutputDirectory}
            label={t("bt.outputDirectory")}
          />
          <Select
            label={t("cloudDownload.workflow.label")}
            placeholder={t("cloudDownload.workflow.default")}
            selectedKeys={workflowId ? [workflowId] : []}
            onSelectionChange={(keys) => setWorkflowId(Array.from(keys)[0] as string | undefined)}
          >
            {workflows.map((workflow) => (
              <SelectItem key={String(workflow.id)}>{workflowLabel(workflow, t)}</SelectItem>
            ))}
          </Select>
        </div>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}
    </Modal>
  );
}
