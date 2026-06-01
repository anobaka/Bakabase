import type { CreateNotificationConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, Radio, RadioGroup, Textarea } from "@/components/bakaui";
import { getEnumKey } from "@/i18n";
import { AppNotificationSeverity } from "@/sdk/constants";

interface Props {
  value: CreateNotificationConfig;
  onChange: (v: CreateNotificationConfig) => void;
}

const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        description={t<string>("workflow.activity.createNotification.title.description")}
        isRequired
        label={t<string>("workflow.activity.createNotification.title.label")}
        value={value.title}
        onValueChange={(s) => onChange({ ...value, title: s })}
      />
      <Textarea
        description={t<string>("workflow.activity.createNotification.body.description")}
        label={t<string>("workflow.activity.createNotification.body.label")}
        minRows={2}
        value={value.body}
        onValueChange={(s) => onChange({ ...value, body: s })}
      />
      {/* Four fixed choices read as a horizontal RadioGroup. */}
      <RadioGroup
        label={t<string>("workflow.activity.createNotification.severity.label")}
        orientation="horizontal"
        value={String(value.severity)}
        onValueChange={(v) =>
          onChange({ ...value, severity: Number(v) as AppNotificationSeverity })
        }
      >
        {(
          [
            [AppNotificationSeverity.Info, "Info"],
            [AppNotificationSeverity.Success, "Success"],
            [AppNotificationSeverity.Warning, "Warning"],
            [AppNotificationSeverity.Error, "Error"],
          ] as const
        ).map(([sev, label]) => (
          <Radio key={sev} value={String(sev)}>
            {t<string>(getEnumKey("AppNotificationSeverity", label), { defaultValue: label })}
          </Radio>
        ))}
      </RadioGroup>
    </div>
  );
};

export default ConfigForm;
