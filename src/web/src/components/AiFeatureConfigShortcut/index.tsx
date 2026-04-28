"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Popover,
  PopoverTrigger,
  PopoverContent,
  Button,
  Input,
} from "@heroui/react";
import { AiOutlineSetting, AiOutlineWarning } from "react-icons/ai";

import { toast, Select, RadioGroup, Radio, Autocomplete, AutocompleteItem } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAiFeatureConfigDbModel,
  BakabaseModulesAIModelsDbLlmProviderConfigDbModel,
  BakabaseModulesAIModelsDomainLlmModelInfo,
} from "@/sdk/Api";
import { AiFeature, AiFeatureLabel } from "@/sdk/constants";

type FeatureConfig = BakabaseModulesAIModelsDbAiFeatureConfigDbModel;
type LlmProviderConfig = BakabaseModulesAIModelsDbLlmProviderConfigDbModel;
type LlmModelInfo = BakabaseModulesAIModelsDomainLlmModelInfo;

type ConfigMode = "default" | "custom";

type Props = {
  feature: AiFeature;
  label?: string | boolean;
};

const configToEditing = (cfg: FeatureConfig | null): Partial<FeatureConfig> =>
  cfg
    ? {
      providerConfigId: cfg.providerConfigId,
      modelId: cfg.modelId,
      temperature: cfg.temperature,
      maxTokens: cfg.maxTokens,
      topP: cfg.topP,
    }
    : {};

const AiFeatureConfigShortcut = ({ feature, label }: Props) => {
  const { t } = useTranslation();
  const resolvedLabel = label === false ? null : (label || t<string>("resource.ai.modelConfig"));
  const isDefault = feature === AiFeature.Default;

  const [featureConfig, setFeatureConfig] = useState<FeatureConfig | null>(null);
  const [defaultConfig, setDefaultConfig] = useState<FeatureConfig | null>(null);
  const [providers, setProviders] = useState<LlmProviderConfig[]>([]);
  const [models, setModels] = useState<LlmModelInfo[]>([]);
  const [loadingModels, setLoadingModels] = useState(false);
  const [editing, setEditing] = useState<Partial<FeatureConfig>>({});
  const [isOpen, setIsOpen] = useState(false);
  const [mode, setMode] = useState<ConfigMode>("default");

  const loadFeatureConfig = useCallback(async () => {
    const r = await BApi.ai.getAiFeatureConfig(feature);
    if (!r.code) {
      const cfg = r.data ?? null;
      setFeatureConfig(cfg);
      if (!isDefault) {
        // If no config or useDefault is true, use default mode
        setMode(!cfg || cfg.useDefault ? "default" : "custom");
      }
    }
  }, [feature, isDefault]);

  const loadDefaultConfig = useCallback(async () => {
    const r = await BApi.ai.getAiFeatureConfig(AiFeature.Default);
    if (!r.code) {
      setDefaultConfig(r.data ?? null);
    }
  }, []);

  const loadProviders = useCallback(async () => {
    const r = await BApi.ai.getAllLlmProviders();
    if (!r.code && r.data) {
      setProviders(r.data.filter((p) => p.isEnabled));
    }
  }, []);

  const loadModels = useCallback(async (providerId: number) => {
    setLoadingModels(true);
    try {
      const r = await BApi.ai.getLlmProviderModels(providerId);
      if (!r.code && r.data) {
        setModels(r.data);
      }
    } finally {
      setLoadingModels(false);
    }
  }, []);

  useEffect(() => {
    loadFeatureConfig();
    loadProviders();
    if (!isDefault) {
      loadDefaultConfig();
    }
  }, [feature]);

  useEffect(() => {
    if (isOpen) {
      loadProviders();
    }
  }, [isOpen]);

  // Sync editing state when mode or source configs change
  useEffect(() => {
    if (isDefault) {
      setEditing(configToEditing(featureConfig));
    } else {
      setEditing(configToEditing(mode === "custom" ? featureConfig : defaultConfig));
    }
  }, [mode, featureConfig, defaultConfig, isDefault]);

  useEffect(() => {
    if (editing.providerConfigId) {
      loadModels(editing.providerConfigId);
    } else {
      setModels([]);
    }
  }, [editing.providerConfigId]);

  const handleSave = async () => {
    if (!isDefault && mode === "default") {
      // In default mode: save editing to the Default feature config,
      // and save the feature config with useDefault=true (preserving custom settings)
      const defaultSave = BApi.ai.saveAiFeatureConfig(AiFeature.Default, {
        useDefault: false,
        providerConfigId: editing.providerConfigId,
        modelId: editing.modelId,
        temperature: editing.temperature,
        maxTokens: editing.maxTokens,
        topP: editing.topP,
      });
      // Mark the feature as "use default" — preserve its existing custom config values
      const featureSave = BApi.ai.saveAiFeatureConfig(feature, {
        useDefault: true,
        providerConfigId: featureConfig?.providerConfigId,
        modelId: featureConfig?.modelId,
        temperature: featureConfig?.temperature,
        maxTokens: featureConfig?.maxTokens,
        topP: featureConfig?.topP,
      });
      const [dr, fr] = await Promise.all([defaultSave, featureSave]);
      if (!dr.code && !fr.code) {
        setDefaultConfig(dr.data ?? null);
        setFeatureConfig(fr.data ?? null);
        toast.success(t<string>("common.success.saved"));
        setIsOpen(false);
      }
    } else {
      // Default feature or custom mode: save directly
      const r = await BApi.ai.saveAiFeatureConfig(feature, {
        useDefault: false,
        providerConfigId: editing.providerConfigId,
        modelId: editing.modelId,
        temperature: editing.temperature,
        maxTokens: editing.maxTokens,
        topP: editing.topP,
      });
      if (!r.code) {
        setFeatureConfig(r.data ?? null);
        if (isDefault) setDefaultConfig(r.data ?? null);
        toast.success(t<string>("common.success.saved"));
        setIsOpen(false);
      }
    }
  };

  const handleClear = async () => {
    const r = await BApi.ai.deleteAiFeatureConfig(feature);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      setFeatureConfig(null);
      if (isDefault) {
        setDefaultConfig(null);
      } else {
        setMode("default");
      }
      setIsOpen(false);
    }
  };

  // Status label reflects the feature's effective config
  const effectivelyUsingDefault = !isDefault && (!featureConfig || featureConfig.useDefault);
  const displayConfig = effectivelyUsingDefault ? defaultConfig : featureConfig;
  const providerName = displayConfig?.providerConfigId
    ? providers.find((p) => p.id === displayConfig.providerConfigId)?.name
    : null;

  const statusLabel = displayConfig?.providerConfigId
    ? (effectivelyUsingDefault ? `${t<string>("configuration.ai.feature.useDefault")}: ` : "")
      + `${providerName ?? displayConfig.providerConfigId} / ${displayConfig.modelId ?? ""}`
    : feature === AiFeature.Default
      ? t<string>("configuration.ai.feature.notConfigured")
      : t<string>("configuration.ai.feature.useDefault");

  const renderConfigForm = () => (
    <>
      <Select
        label={t<string>("configuration.ai.feature.provider")}
        size="sm"
        dataSource={providers.map((p) => ({
          label: p.name,
          value: String(p.id),
        }))}
        selectedKeys={
          editing.providerConfigId
            ? [String(editing.providerConfigId)]
            : undefined
        }
        onSelectionChange={(keys) => {
          const arr = Array.from(keys);
          if (arr.length > 0) {
            setEditing({
              ...editing,
              providerConfigId: Number(arr[0]),
              modelId: undefined,
            });
          }
        }}
      />

      <Autocomplete
        label={t<string>("configuration.ai.feature.model")}
        size="sm"
        isLoading={loadingModels}
        defaultItems={models.map((m) => ({
          label: m.displayName ?? m.modelId ?? "",
          value: m.modelId ?? "",
        }))}
        selectedKey={editing.modelId ?? null}
        onSelectionChange={(key) => {
          if (key != null) {
            setEditing({ ...editing, modelId: String(key) });
          }
        }}
      >
        {(item) => (
          <AutocompleteItem key={item.value}>
            {item.label}
          </AutocompleteItem>
        )}
      </Autocomplete>

      <Input
        label={t<string>("configuration.ai.feature.temperature")}
        size="sm"
        type="number"
        step={0.1}
        min={0}
        max={2}
        value={editing.temperature != null ? String(editing.temperature) : ""}
        onValueChange={(v) =>
          setEditing({ ...editing, temperature: v ? Number(v) : undefined })
        }
      />

      <Input
        label={t<string>("configuration.ai.feature.maxTokens")}
        size="sm"
        type="number"
        step={1}
        min={1}
        value={editing.maxTokens != null ? String(editing.maxTokens) : ""}
        onValueChange={(v) =>
          setEditing({ ...editing, maxTokens: v ? Number(v) : undefined })
        }
      />
    </>
  );

  return (
    <div className="flex items-center gap-2">
      {resolvedLabel && <span className="text-sm text-default-500">{resolvedLabel}</span>}
      <Popover isOpen={isOpen} onOpenChange={setIsOpen} placement="bottom">
        <PopoverTrigger>
          <Button size="sm" variant="flat" startContent={<AiOutlineSetting className="text-lg" />}>
            <span className="text-xs">{statusLabel}</span>
          </Button>
        </PopoverTrigger>
        <PopoverContent>
          <div className="flex flex-col gap-3 p-3 w-[320px]">
            <div className="text-sm font-medium">
              {t<string>(`configuration.ai.feature.${AiFeatureLabel[feature]}`)}
            </div>

            {!isDefault && (
              <RadioGroup
                size="sm"
                orientation="horizontal"
                value={mode}
                onValueChange={(v) => setMode(v as ConfigMode)}
              >
                <Radio value="default">
                  {t<string>("configuration.ai.feature.useDefault")}
                </Radio>
                <Radio value="custom">
                  {t<string>("configuration.ai.feature.useCustom")}
                </Radio>
              </RadioGroup>
            )}

            {!isDefault && mode === "default" && (
              <div className="flex items-start gap-1.5 bg-warning-50 text-warning-700 rounded p-2 text-xs">
                <AiOutlineWarning className="text-sm mt-0.5 shrink-0" />
                <span>{t<string>("configuration.ai.feature.defaultConfigWarning")}</span>
              </div>
            )}

            {renderConfigForm()}

            <div className="flex gap-2 justify-end">
              {(isDefault || mode === "custom") && (
                <Button size="sm" variant="flat" onPress={handleClear}>
                  {t<string>("common.action.clear")}
                </Button>
              )}
              <Button size="sm" color="primary" onPress={handleSave}>
                {t<string>("common.action.save")}
              </Button>
            </div>
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
};

AiFeatureConfigShortcut.displayName = "AiFeatureConfigShortcut";

export default AiFeatureConfigShortcut;
