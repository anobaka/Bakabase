import type { BakabaseModulesWorkflowAbstractionsModelsViewWorkflowDefinitionViewModel as Workflow } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { FiExternalLink, FiGitBranch, FiRefreshCw } from "react-icons/fi";

import { Button, Select, toast } from "@/components/bakaui";
import { workflowLabel } from "@/components/Workflow/builtinLabels";
import BApi from "@/sdk/BApi";

interface Props {
  value?: number | null;
  onChange: (workflowId: number | null) => Promise<void> | void;
}

export default function DownloadResultWorkflowField({ value, onChange }: Props) {
  const { t } = useTranslation();
  const [workflows, setWorkflows] = useState<Workflow[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    let active = true;

    setLoading(true);
    setLoadFailed(false);
    void BApi.workflow
      .searchWorkflows({ triggerKind: "downloader.resultReady", enabledOnly: true })
      .then((response) => {
        if (!active) return;
        setWorkflows(
          (response.data ?? []).filter(
            (workflow) => workflow.enabled && workflow.triggerKind === "downloader.resultReady",
          ),
        );
      })
      .catch(() => {
        if (active) setLoadFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [revision]);

  const unavailable =
    value != null && !loading && !loadFailed && !workflows.some((w) => w.id === value);
  const choices = [
    { value: "none", label: t("thirdPartyConfig.downloadResultWorkflow.saveOnly") },
    ...workflows.map((workflow) => ({
      value: String(workflow.id),
      label: workflowLabel(workflow, t),
    })),
  ];

  // Keep an existing disabled/deleted definition visible until the user chooses a replacement.
  if (value != null && !workflows.some((w) => w.id === value)) {
    choices.push({
      value: String(value),
      label: t("thirdPartyConfig.downloadResultWorkflow.unavailableSelection", { id: value }),
    });
  }

  const save = async (key: string) => {
    const next = key === "none" ? null : Number(key);

    if (next === (value ?? null) || (next != null && !workflows.some((w) => w.id === next))) return;
    setSaving(true);
    setSaveFailed(false);
    try {
      await onChange(next);
      toast.success(t("thirdPartyConfig.success.saved"));
    } catch {
      setSaveFailed(true);
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="space-y-3 rounded-xl bg-default-50 p-3">
      <div className="flex items-center gap-2 text-sm font-medium">
        <FiGitBranch aria-hidden className="shrink-0 text-default-500" />
        {t("thirdPartyConfig.downloadResultWorkflow.title")}
      </div>
      <p className="text-xs leading-relaxed text-default-500">
        {t("thirdPartyConfig.downloadResultWorkflow.description")}
      </p>
      <div className="flex items-center gap-2">
        <Select
          disallowEmptySelection
          className="min-w-0 flex-1"
          dataSource={choices}
          isDisabled={loading || saving || loadFailed}
          isLoading={loading || saving}
          label={t("thirdPartyConfig.downloadResultWorkflow.label")}
          selectedKeys={[value == null ? "none" : String(value)]}
          size="sm"
          onSelectionChange={(keys) => {
            const key = Array.from(keys)[0];

            if (key != null) void save(String(key));
          }}
        />
        <Button
          isIconOnly
          aria-label={t("thirdPartyConfig.downloadResultWorkflow.refresh")}
          isDisabled={loading || saving}
          size="sm"
          variant="light"
          onPress={() => setRevision((r) => r + 1)}
        >
          <FiRefreshCw aria-hidden size={16} />
        </Button>
      </div>
      {loadFailed && (
        <p className="text-xs text-danger" role="alert">
          {t("thirdPartyConfig.downloadResultWorkflow.loadFailed")}
        </p>
      )}
      {saveFailed && (
        <p className="text-xs text-danger" role="alert">
          {t("thirdPartyConfig.downloadResultWorkflow.saveFailed")}
        </p>
      )}
      {unavailable && (
        <p className="text-xs text-warning" role="alert">
          {t("thirdPartyConfig.downloadResultWorkflow.unavailable")}
        </p>
      )}
      {value != null && (
        <Button
          as="a"
          href={`#/workflows/editor?id=${value}`}
          rel="noopener noreferrer"
          size="sm"
          startContent={<FiExternalLink aria-hidden size={14} />}
          target="_blank"
          variant="light"
        >
          {t("thirdPartyConfig.downloadResultWorkflow.view")}
        </Button>
      )}
      <p className="text-xs leading-relaxed text-default-500">
        {t("thirdPartyConfig.downloadResultWorkflow.scope")}
      </p>
    </div>
  );
}
