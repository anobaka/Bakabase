"use client";

import type { MarkConfig } from "../types";
import { Input, RadioGroup, Radio } from "@/components/bakaui";
import { PathMatchMode } from "@/sdk/constants";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

const MatchModeSelector = ({ config, updateConfig, t }: Props) => (
  <>
    <div className="flex flex-col gap-2">
      <span className="text-sm font-medium">{t("Match Mode")}</span>
      <RadioGroup
        value={String(config.matchMode)}
        onValueChange={(value) => updateConfig({ matchMode: Number(value) })}
        size="sm"
        orientation="horizontal"
      >
        <Radio value={String(PathMatchMode.Layer)} description={t("Match items at a specific folder level relative to this path")}>
          {t("Layer")}
        </Radio>
        <Radio value={String(PathMatchMode.Regex)} description={t("Match items whose path matches the regular expression pattern")}>
          {t("Regex")}
        </Radio>
      </RadioGroup>
    </div>

    {config.matchMode === PathMatchMode.Layer ? (
      <Input
        label={t("Layer")}
        description={t("0 = current item, 1 = direct children, 2 = grandchildren, etc.")}
        type="number"
        size="sm"
        value={String(config.layer ?? 0)}
        onValueChange={(v) => updateConfig({ layer: parseInt(v) || 0 })}
      />
    ) : (
      <Input
        label={t("Regex Pattern")}
        description={t("Regular expression to match against file/folder paths")}
        size="sm"
        value={config.regex ?? ""}
        onValueChange={(v) => updateConfig({ regex: v })}
      />
    )}
  </>
);

MatchModeSelector.displayName = "MatchModeSelector";

export default MatchModeSelector;
