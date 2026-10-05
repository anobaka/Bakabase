"use client";

import type { WorkflowActivityUI } from "../types";
import type { components } from "@/sdk/BApi2";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Checkbox, Chip, Input, Snippet, Spinner, Switch } from "@/components/bakaui";
import { AcquisitionWaitReason, WorkflowActivityCategory } from "@/sdk/constants";

type InboxCandidate =
  components["schemas"]["Bakabase.Service.Components.Acquisition.InboxCandidate"];

interface Config {
  openLink: boolean;
}

interface Prompt {
  url: string | null;
  accessCode: string | null;
  expectedFileName: string | null;
  inboxDirectory: string | null;
  waitingSince: string;
}

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Switch
      isSelected={value.openLink !== false}
      size="sm"
      onValueChange={(openLink) => onChange({ ...value, openLink })}
    >
      <span className="text-sm">{t<string>("workflow.acquisition.waitForInbox.openLink")}</span>
    </Switch>
  );
};

/**
 * The half of the pipeline a person does. It shows the link and the access code, and lists what is
 * sitting in the inbox with the watcher's own score beside each file — so a claim the watcher was
 * not confident enough to make is one click away, and the reason it hesitated is visible.
 */
const ResumeForm: WorkflowActivityUI<Config>["ResumeForm"] = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const [candidates, setCandidates] = React.useState<InboxCandidate[] | null>(null);
  const [files, setFiles] = React.useState<string[]>([]);
  const [directory, setDirectory] = React.useState("");
  const [alreadyProcessed, setAlreadyProcessed] = React.useState(false);
  const submit = (selection: { files: string[]; directory?: string }) =>
    onSubmit(
      JSON.stringify({
        reason: AcquisitionWaitReason.WaitingForFile,
        payloadJson: JSON.stringify({ ...selection, alreadyProcessed }),
      }),
    );

  React.useEffect(() => {
    void BApi.acquisition
      .getAcquisitionInbox()
      .then((r) => setCandidates((r.data ?? []) as InboxCandidate[]));
  }, []);

  let prompt: Prompt | null = null;

  try {
    prompt = promptJson ? (JSON.parse(promptJson) as Prompt) : null;
  } catch {
    prompt = null;
  }

  return (
    <div className="flex flex-col gap-3">
      {prompt?.url && (
        <div className="flex items-center gap-2">
          <Button
            as="a"
            href={prompt.url}
            rel="noreferrer"
            size="sm"
            target="_blank"
            variant="flat"
          >
            {t<string>("workflow.acquisition.waitForInbox.open")}
          </Button>
          {prompt.accessCode && (
            <Snippet size="sm" symbol="">
              {prompt.accessCode}
            </Snippet>
          )}
        </div>
      )}

      {prompt?.inboxDirectory && (
        <div className="text-xs text-default-400">
          {t<string>("workflow.acquisition.waitForInbox.saveTo", {
            directory: prompt.inboxDirectory,
          })}
        </div>
      )}

      {candidates == null ? (
        <Spinner size="sm" />
      ) : candidates.length === 0 ? (
        <div className="text-xs text-default-500">
          {t<string>("workflow.acquisition.waitForInbox.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {candidates.map((c) => (
            <div key={c.path} className="flex items-center gap-2">
              <Checkbox
                aria-label={t("workflow.processing.selectFile", { name: c.fileName })}
                isDisabled={submitting || !c.isStable || !!directory.trim()}
                isSelected={files.includes(c.path!)}
                onValueChange={(checked) =>
                  setFiles((current) =>
                    checked ? [...current, c.path!] : current.filter((path) => path !== c.path),
                  )
                }
              />
              <span className="flex-1 truncate text-xs">{c.fileName}</span>
              {!c.isStable && (
                <Chip color="warning" size="sm" variant="flat">
                  {t<string>("workflow.acquisition.waitForInbox.stillArriving")}
                </Chip>
              )}
              <Button
                color="primary"
                isDisabled={submitting || !c.isStable}
                size="sm"
                variant="flat"
                onPress={() => submit({ files: [c.path!] })}
              >
                {t<string>("workflow.acquisition.waitForInbox.claim")}
              </Button>
            </div>
          ))}
        </div>
      )}
      <Input
        description={t("workflow.processing.inboxDirectoryHint")}
        label={t("workflow.processing.inboxDirectory")}
        size="sm"
        value={directory}
        onValueChange={setDirectory}
      />
      <Switch isSelected={alreadyProcessed} size="sm" onValueChange={setAlreadyProcessed}>
        {t("workflow.processing.alreadyProcessed")}
      </Switch>
      <Button
        color="primary"
        isDisabled={submitting || (!directory.trim() && files.length === 0)}
        size="sm"
        onPress={() =>
          submit(directory.trim() ? { files: [], directory: directory.trim() } : { files })
        }
      >
        {t("workflow.processing.claimSelection", {
          count: directory.trim() ? t("workflow.processing.wholeDirectory") : files.length,
        })}
      </Button>
    </div>
  );
};

export const AcquisitionWaitForInboxUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.waitForInbox",
  displayNameKey: "workflow.acquisition.step.waitForInbox",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ openLink: true }),
  parseConfig: (json) => {
    try {
      return json
        ? { openLink: true, ...(JSON.parse(json) as Partial<Config>) }
        : { openLink: true };
    } catch {
      return { openLink: true };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary: () => null,
  ResumeForm,
};
