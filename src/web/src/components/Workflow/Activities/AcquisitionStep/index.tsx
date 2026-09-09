import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Textarea } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

/** Every acquisition step's kind starts with this. */
const PREFIX = "acquisition.";

/**
 * One node package standing in for all of the acquisition steps.
 *
 * The steps arrive over several releases and most take a small, flat configuration, so a package
 * per step would be a dozen near-identical files before any of them earned its own form. This one
 * covers all of them by kind prefix and shows the raw configuration; a step that deserves a proper
 * form gets its own entry in the registry, which wins over this fallback.
 */
const RawConfigForm: React.FC<{
  value: Record<string, unknown>;
  onChange: (v: Record<string, unknown>) => void;
}> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const [text, setText] = React.useState(() => JSON.stringify(value ?? {}, null, 2));
  const [invalid, setInvalid] = React.useState(false);

  return (
    <Textarea
      description={
        invalid
          ? t<string>("workflow.acquisition.config.invalid")
          : t<string>("workflow.acquisition.config.hint")
      }
      isInvalid={invalid}
      label={t<string>("workflow.acquisition.config.label")}
      minRows={3}
      value={text}
      onValueChange={(next) => {
        setText(next);
        try {
          onChange(JSON.parse(next || "{}"));
          setInvalid(false);
        } catch {
          // Kept out of the draft until it parses: half-typed JSON is not a configuration.
          setInvalid(true);
        }
      }}
    />
  );
};

export function acquisitionStepUI(kind: string): WorkflowActivityUI<Record<string, unknown>> {
  return {
    kind,
    displayNameKey: `workflow.acquisition.step.${kind.slice(PREFIX.length)}`,
    category: WorkflowActivityCategory.Action,
    defaultConfig: () => ({}),
    parseConfig: (json) => {
      try {
        return json ? JSON.parse(json) : {};
      } catch {
        return {};
      }
    },
    serializeConfig: (config) => JSON.stringify(config ?? {}),
    isValid: () => true,
    ConfigForm: RawConfigForm,
    Summary: () => null,
  };
}

export const isAcquisitionStepKind = (kind: string) => kind.startsWith(PREFIX);
