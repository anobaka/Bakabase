"use client";

import type { MarkConfig } from "../types";
import { Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PathMatchMode } from "@/sdk/constants";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
  priority: number;
  onPriorityChange: (priority: number) => void;
};

const MatchModeSelector = ({ config, updateConfig, t, priority, onPriorityChange }: Props) => {
  const isLayerMode = config.matchMode === PathMatchMode.Layer;

  return (
    <>
      <div className="flex flex-col gap-1">
        <span className="text-sm font-medium">{t("Match Mode")}</span>
        <RadioGroup
          value={String(config.matchMode)}
          onValueChange={(value) => updateConfig({ matchMode: Number(value) })}
          size="sm"
          orientation="horizontal"
        >
          <Radio value={String(PathMatchMode.Layer)}>
            {t("Layer")}
          </Radio>
          <Radio value={String(PathMatchMode.Regex)}>
            {t("Regex")}
          </Radio>
        </RadioGroup>
      </div>

      <div className="flex gap-2 items-start">
        {isLayerMode ? (
          <NumberInput
            label={t("Layer")}
            description={t("PathMark.Layer.Description")}
            size="sm"
            className="flex-1"
            value={config.layer ?? 0}
            onChange={(e) => updateConfig({ layer: typeof e === "number" ? e : (e.target.value ? parseInt(e.target.value, 10) : 0) })}
          />
        ) : (
          <Input
            label={t("Regex Pattern")}
            description={t("PathMark.Regex.Description")}
            size="sm"
            className="flex-1"
            value={config.regex ?? ""}
            onValueChange={(v) => updateConfig({ regex: v })}
          />
        )}
        <NumberInput
          label={t("Priority")}
          size="sm"
          className="w-28"
          value={priority}
          onValueChange={(v) => onPriorityChange(v ?? 10)}
        />
      </div>
    </>
  );
};

MatchModeSelector.displayName = "MatchModeSelector";

export default MatchModeSelector;
