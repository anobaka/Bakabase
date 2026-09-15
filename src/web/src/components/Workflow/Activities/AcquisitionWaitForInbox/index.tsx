"use client";

import type { WorkflowActivityUI } from "../types";
import type { components } from "@/sdk/BApi2";

import React from "react";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import { Button, Chip, Snippet, Spinner, Switch } from "@/components/bakaui";
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
                onPress={() =>
                  onSubmit(
                    JSON.stringify({
                      reason: AcquisitionWaitReason.WaitingForFile,
                      payloadJson: JSON.stringify({ files: [c.path] }),
                    }),
                  )
                }
              >
                {t<string>("workflow.acquisition.waitForInbox.claim")}
              </Button>
            </div>
          ))}
        </div>
      )}
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
