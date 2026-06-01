import type { ExHentaiEnqueueDownloadConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, Switch } from "@/components/bakaui";

interface Props {
  value: ExHentaiEnqueueDownloadConfig;
  onChange: (v: ExHentaiEnqueueDownloadConfig) => void;
}

const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Input
        description={t<string>("workflow.activity.exhentaiEnqueue.intervalMs.description")}
        label={t<string>("workflow.activity.exhentaiEnqueue.intervalMs.label")}
        min={1000}
        type="number"
        value={String(value.intervalMs)}
        onValueChange={(v) => {
          const n = Number(v);
          if (!isNaN(n)) onChange({ ...value, intervalMs: Math.max(1000, n) });
        }}
      />
      <Switch
        isSelected={value.autoRetry}
        onValueChange={(b) => onChange({ ...value, autoRetry: b })}
      >
        {t<string>("workflow.activity.exhentaiEnqueue.autoRetry.label")}
      </Switch>
    </div>
  );
};

export default ConfigForm;
