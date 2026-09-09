"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Input, Switch } from "@/components/bakaui";
import {
  AcquisitionDriveKindLabel,
  AcquisitionWaitReason,
  WorkflowActivityCategory,
} from "@/sdk/constants";

interface Config {
  alwaysAsk: boolean;
}

interface PromptLink {
  index: number;
  url: string;
  driveKind: number;
  accessCode: string | null;
}

interface Prompt {
  links: PromptLink[];
  why: string | null;
}

const signalOf = (reason: AcquisitionWaitReason, payload: unknown) =>
  JSON.stringify({ reason, payloadJson: JSON.stringify(payload) });

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <Switch
      isSelected={!!value.alwaysAsk}
      size="sm"
      onValueChange={(alwaysAsk) => onChange({ ...value, alwaysAsk })}
    >
      <span className="text-sm">{t<string>("workflow.acquisition.selectLink.alwaysAsk")}</span>
    </Switch>
  );
};

/**
 * Two questions in one form, because they are the same question at different times: pick one of
 * the links the post offered, or — when it offered none — paste the one you found yourself.
 */
const ResumeForm: WorkflowActivityUI<Config>["ResumeForm"] = ({
  promptJson,
  submitting,
  onSubmit,
}) => {
  const { t } = useTranslation();
  const [typed, setTyped] = React.useState("");

  let prompt: Prompt | null = null;

  try {
    prompt = promptJson ? (JSON.parse(promptJson) as Prompt) : null;
  } catch {
    prompt = null;
  }

  const links = prompt?.links ?? [];

  return (
    <div className="flex flex-col gap-3">
      {prompt?.why && <div className="text-sm text-default-500">{prompt.why}</div>}

      {links.length > 0 && (
        <div className="flex flex-col gap-1">
          {links.map((l) => (
            <div key={l.index} className="flex items-center gap-2">
              <Chip size="sm" variant="flat">
                {AcquisitionDriveKindLabel[l.driveKind as never] ?? l.driveKind}
              </Chip>
              <span className="flex-1 truncate text-xs text-default-500">{l.url}</span>
              {l.accessCode && (
                <span className="text-xs text-default-400">
                  {t<string>("workflow.acquisition.selectLink.code", { code: l.accessCode })}
                </span>
              )}
              <Button
                color="primary"
                isDisabled={submitting}
                size="sm"
                variant="flat"
                onPress={() =>
                  onSubmit(signalOf(AcquisitionWaitReason.ChooseLink, { selectedIndex: l.index }))
                }
              >
                {t<string>("workflow.acquisition.selectLink.use")}
              </Button>
            </div>
          ))}
        </div>
      )}

      <div className="flex items-end gap-2">
        <Input
          className="flex-1"
          description={t<string>("workflow.acquisition.selectLink.paste.description")}
          label={t<string>("workflow.acquisition.selectLink.paste.label")}
          size="sm"
          value={typed}
          onValueChange={setTyped}
        />
        <Button
          isDisabled={submitting || typed.trim().length === 0}
          size="sm"
          onPress={() =>
            onSubmit(
              signalOf(AcquisitionWaitReason.NoLinks, {
                selectedIndex: 0,
                links: [{ url: typed.trim() }],
              }),
            )
          }
        >
          {t<string>("workflow.acquisition.selectLink.use")}
        </Button>
      </div>
    </div>
  );
};

export const AcquisitionSelectLinkUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.selectLink",
  displayNameKey: "workflow.acquisition.step.selectLink",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ alwaysAsk: false }),
  parseConfig: (json) => {
    try {
      return json
        ? { alwaysAsk: false, ...(JSON.parse(json) as Partial<Config>) }
        : { alwaysAsk: false };
    } catch {
      return { alwaysAsk: false };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  isValid: () => true,
  ConfigForm,
  Summary: ({ config }) => {
    const { t } = useTranslation();

    return config.alwaysAsk ? (
      <span className="text-xs text-default-500">
        {t<string>("workflow.acquisition.selectLink.alwaysAsk")}
      </span>
    ) : null;
  },
  ResumeForm,
};
