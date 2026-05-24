"use client";

import type { MarkConfig } from "../types";

import { Input, NumberInput, RadioGroup, Radio } from "@/components/bakaui";
import { PathMatchMode, PathMarkApplyScope, PathMarkType } from "@/sdk/constants";

type Props = {
  config: MarkConfig;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
  markType: PathMarkType;
};

const markTypeKey = (markType: PathMarkType) => {
  switch (markType) {
    case PathMarkType.Resource:
      return "resource";
    case PathMarkType.Property:
      return "property";
    case PathMarkType.MediaLibrary:
      return "mediaLibrary";
    default:
      return "resource";
  }
};

const MatchModeSelector = ({ config, updateConfig, t, markType }: Props) => {
  const isLayerMode = config.matchMode === PathMatchMode.Layer;
  const currentApplyScope = config.applyScope ?? PathMarkApplyScope.MatchedOnly;
  const typeKey = markTypeKey(markType);

  return (
    <>
      <div className="flex flex-col gap-1">
        <RadioGroup
          label={t("pathMarkConfig.label.matchMode")}
          orientation="horizontal"
          size="sm"
          value={String(config.matchMode)}
          onValueChange={(value) => updateConfig({ matchMode: Number(value) })}
        >
          <Radio value={String(PathMatchMode.Layer)}>{t("pathMarkConfig.label.layer")}</Radio>
          <Radio value={String(PathMatchMode.Regex)}>{t("pathMarkConfig.label.regex")}</Radio>
        </RadioGroup>
        <div className="text-xs text-default-400">
          {t(`pathMark.${typeKey}.matchMode.description`)}
        </div>
      </div>

      {isLayerMode ? (
        <NumberInput
          description={t("pathMark.layer.description")}
          label={t("pathMarkConfig.label.layer")}
          size="sm"
          value={config.layer ?? 0}
          onChange={(e) =>
            updateConfig({
              layer: typeof e === "number" ? e : e.target.value ? parseInt(e.target.value, 10) : 0,
            })
          }
        />
      ) : (
        <Input
          description={t("pathMark.regex.description")}
          label={t("pathMarkConfig.label.regexPattern")}
          size="sm"
          value={config.regex ?? ""}
          onValueChange={(v) => updateConfig({ regex: v })}
        />
      )}

      <div className="flex flex-col gap-1">
        <RadioGroup
          label={t("pathMarkConfig.label.applyScope")}
          orientation="horizontal"
          size="sm"
          value={String(currentApplyScope)}
          onValueChange={(value) =>
            updateConfig({ applyScope: Number(value) as PathMarkApplyScope })
          }
        >
          <Radio
            description={t(`pathMark.${typeKey}.applyScope.matchedOnly.description`)}
            value={String(PathMarkApplyScope.MatchedOnly)}
          >
            {t("pathMarkConfig.label.matchedOnly")}
          </Radio>
          <Radio
            description={t(`pathMark.${typeKey}.applyScope.matchedAndSubdirectories.description`)}
            value={String(PathMarkApplyScope.MatchedAndSubdirectories)}
          >
            {t("pathMarkConfig.label.matchedAndSubdirectories")}
          </Radio>
        </RadioGroup>
      </div>
    </>
  );
};

MatchModeSelector.displayName = "MatchModeSelector";

export default MatchModeSelector;
