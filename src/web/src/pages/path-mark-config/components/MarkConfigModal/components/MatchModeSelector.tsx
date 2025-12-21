"use client";

import type { MarkConfig } from "../types";
import { Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PathMatchMode, PathMarkApplyScope } from "@/sdk/constants";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

const MatchModeSelector = ({ config, updateConfig, t }: Props) => {
  const isLayerMode = config.matchMode === PathMatchMode.Layer;
  const currentApplyScope = config.applyScope ?? PathMarkApplyScope.MatchedOnly;

  return (
    <>
      <div className="flex flex-col gap-1">
        <RadioGroup
          value={String(config.matchMode)}
          onValueChange={(value) => updateConfig({ matchMode: Number(value) })}
          size="sm"
          orientation="horizontal"
          label={t("Match Mode")}
        >
          <Radio value={String(PathMatchMode.Layer)}>
            {t("Layer")}
          </Radio>
          <Radio value={String(PathMatchMode.Regex)}>
            {t("Regex")}
          </Radio>
        </RadioGroup>
      </div>

      {isLayerMode ? (
        <NumberInput
          label={t("Layer")}
          description={t("PathMark.Layer.Description")}
          size="sm"
          value={config.layer ?? 0}
          onChange={(e) => updateConfig({ layer: typeof e === "number" ? e : (e.target.value ? parseInt(e.target.value, 10) : 0) })}
        />
      ) : (
        <Input
          label={t("Regex Pattern")}
          description={t("PathMark.Regex.Description")}
          size="sm"
          value={config.regex ?? ""}
          onValueChange={(v) => updateConfig({ regex: v })}
        />
      )}

      <div className="flex flex-col gap-1">
        <RadioGroup
          value={String(currentApplyScope)}
          onValueChange={(value) => updateConfig({ applyScope: Number(value) as PathMarkApplyScope })}
          size="sm"
          label={t("Apply Scope")}
          orientation="horizontal"
        >
          <Radio
            value={String(PathMarkApplyScope.MatchedOnly)}
            description={t("PathMark.ApplyScope.MatchedOnly.Description")}
          >
            {t("MatchedOnly")}
          </Radio>
          <Radio
            value={String(PathMarkApplyScope.MatchedAndSubdirectories)}
            description={t("PathMark.ApplyScope.MatchedAndSubdirectories.Description")}
          >
            {t("MatchedAndSubdirectories")}
          </Radio>
        </RadioGroup>
      </div>
    </>
  );
};

MatchModeSelector.displayName = "MatchModeSelector";

export default MatchModeSelector;
