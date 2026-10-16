import type { MarkConfig } from "../types";

import { Button, Input, NumberInput, Radio, RadioGroup } from "@/components/bakaui";
import { PathMarkType, PathMarkValueBasePath, PathMatchMode } from "@/sdk/constants";

type Props = {
  config: MarkConfig;
  markType: PathMarkType;
  updateConfig: (updates: Partial<MarkConfig>) => void;
  t: (key: string) => string;
};

const DynamicValueConfig = ({ config, markType, updateConfig, t }: Props) => {
  const isLibrary = markType === PathMarkType.MediaLibrary;
  const basePath = config.valueBasePath ?? PathMarkValueBasePath.MarkPath;
  const mode = config.valueMatchMode ?? PathMatchMode.Layer;
  const layer = isLibrary ? config.layerToMediaLibrary : config.valueLayer;
  const regex = (isLibrary ? config.regexToMediaLibrary : config.valueRegex) ?? "";
  const legacyRegex = !isLibrary && config.valueRegexMatchesResourcePath;
  const invalidLayer = typeof layer !== "number" || !Number.isInteger(layer);
  const resourcePositiveLayer = basePath === PathMarkValueBasePath.ResourcePath && (layer ?? 0) > 0;
  const updateLayer = (value: number | undefined) =>
    updateConfig(isLibrary ? { layerToMediaLibrary: value } : { valueLayer: value });
  const updateRegex = (value: string) =>
    updateConfig(isLibrary ? { regexToMediaLibrary: value } : { valueRegex: value });

  return (
    <div className="flex flex-col gap-4">
      {legacyRegex ? (
        <div className="rounded-xl border border-warning-200 bg-warning-50 p-3 text-sm text-warning-800">
          <div className="font-medium">{t("pathMarkConfig.value.legacyTitle")}</div>
          <p className="mt-1 text-xs leading-relaxed">
            {t("pathMarkConfig.value.legacyDescription")}
          </p>
          <Button
            className="mt-2"
            size="sm"
            variant="flat"
            onPress={() =>
              updateConfig({
                valueRegexMatchesResourcePath: false,
                valueBasePath: PathMarkValueBasePath.ResourcePath,
                valueMatchMode: PathMatchMode.Regex,
              })
            }
          >
            {t("pathMarkConfig.value.convertLegacy")}
          </Button>
        </div>
      ) : (
        <>
          <RadioGroup
            label={t("pathMarkConfig.value.basePath")}
            orientation="horizontal"
            size="sm"
            value={String(basePath)}
            onValueChange={(value) => updateConfig({ valueBasePath: Number(value) })}
          >
            <Radio
              description={t("pathMarkConfig.value.markPathDescription")}
              value={String(PathMarkValueBasePath.MarkPath)}
            >
              {t("pathMarkConfig.value.markPath")}
            </Radio>
            <Radio
              description={t("pathMarkConfig.value.resourcePathDescription")}
              value={String(PathMarkValueBasePath.ResourcePath)}
            >
              {t("pathMarkConfig.value.resourcePath")}
            </Radio>
          </RadioGroup>
          <RadioGroup
            label={t("pathMarkConfig.label.valueExtractionMode")}
            orientation="horizontal"
            size="sm"
            value={String(mode)}
            onValueChange={(value) => {
              const valueMatchMode = Number(value) as PathMatchMode;

              updateConfig({ valueMatchMode });
              if (valueMatchMode === PathMatchMode.Layer && layer === undefined) updateLayer(0);
            }}
          >
            <Radio value={String(PathMatchMode.Layer)}>{t("pathMarkConfig.value.byLayer")}</Radio>
            <Radio value={String(PathMatchMode.Regex)}>{t("pathMarkConfig.value.byRegex")}</Radio>
          </RadioGroup>
        </>
      )}

      {mode === PathMatchMode.Layer && !legacyRegex ? (
        <>
          <NumberInput
            description={t(
              basePath === PathMarkValueBasePath.ResourcePath
                ? "pathMarkConfig.value.resourceLayerDescription"
                : "pathMarkConfig.value.markLayerDescription",
            )}
            errorMessage={t(
              resourcePositiveLayer
                ? "pathMarkConfig.value.positiveResourceLayer"
                : "pathMarkConfig.error.valueLayerRequired",
            )}
            isInvalid={invalidLayer || resourcePositiveLayer}
            label={t("pathMarkConfig.label.valueLayer")}
            size="sm"
            step={1}
            value={invalidLayer ? undefined : layer}
            onValueChange={(value) => updateLayer(Number.isFinite(value) ? value : undefined)}
          />
          {resourcePositiveLayer && (
            <div className="flex flex-wrap gap-2">
              <Button size="sm" variant="flat" onPress={() => updateLayer(0)}>
                {t("pathMarkConfig.value.useSelf")}
              </Button>
              <Button size="sm" variant="flat" onPress={() => updateLayer(-1)}>
                {t("pathMarkConfig.value.useParent")}
              </Button>
            </div>
          )}
        </>
      ) : (
        <Input
          description={t(
            legacyRegex
              ? "pathMarkConfig.value.legacyRegexDescription"
              : "pathMarkConfig.value.regexDescription",
          )}
          errorMessage={t("pathMarkConfig.error.valueRegexRequired")}
          isInvalid={!regex.trim()}
          label={t("pathMarkConfig.label.valueRegex")}
          placeholder={t("pathMarkConfig.tip.regexExample")}
          size="sm"
          value={regex}
          onValueChange={updateRegex}
        />
      )}
      <p className="rounded-lg bg-default-100 px-3 py-2 text-xs leading-relaxed text-default-500">
        {t("pathMarkConfig.value.singleValueNote")}
      </p>
    </div>
  );
};

export default DynamicValueConfig;
