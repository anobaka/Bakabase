import type { DownloaderEnqueueConfig } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, Switch } from "@/components/bakaui";

interface Props {
  value: DownloaderEnqueueConfig;
  onChange: (v: DownloaderEnqueueConfig) => void;
}

const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <div className="text-xs text-default-500">
        {t<string>("workflow.activity.downloaderEnqueue.description")}
      </div>
      <Input
        description={t<string>("workflow.activity.downloaderEnqueue.intervalMs.description")}
        label={t<string>("workflow.activity.downloaderEnqueue.intervalMs.label")}
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
        {t<string>("workflow.activity.downloaderEnqueue.autoRetry.label")}
      </Switch>
    </div>
  );
};

export default ConfigForm;
