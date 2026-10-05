"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Checkbox, Input, Modal, Tab, Tabs } from "@/components/bakaui";
import { AiFeature } from "@/sdk/constants";
import {
  SoulPlusConfigPanel,
  SoulPlusConfigField,
} from "@/components/ThirdPartyConfig/platforms/SoulPlusConfig";
import AiProviderPanel from "@/components/AiProviderPanel";
import AiFeaturePanel from "@/components/AiFeaturePanel";
import BApi from "@/sdk/BApi";
import { useThirdPartyOptionsStore } from "@/stores/options";

type Props = DestroyableProps;
type ParserOptionsPatch = Pick<
  Parameters<typeof BApi.options.patchThirdPartyOptions>[0],
  "automaticallyParsingPosts" | "postParserMaxConcurrency" | "postParserAiMaxConcurrency"
>;
const ConfigurationModal = (props: Props) => {
  const { t } = useTranslation();
  const automaticallyParsing = useThirdPartyOptionsStore(
    (state) => state.data.automaticallyParsingPosts,
  );
  const maxConcurrency = useThirdPartyOptionsStore(
    (state) => state.data.postParserMaxConcurrency ?? 10,
  );
  const aiMaxConcurrency = useThirdPartyOptionsStore(
    (state) => state.data.postParserAiMaxConcurrency ?? 1,
  );
  const [limits, setLimits] = useState({
    total: String(maxConcurrency),
    ai: String(aiMaxConcurrency),
  });

  useEffect(
    () => setLimits({ total: String(maxConcurrency), ai: String(aiMaxConcurrency) }),
    [maxConcurrency, aiMaxConcurrency],
  );
  const validLimits = [limits.total, limits.ai].every(
    (value) =>
      Number.isSafeInteger(Number(value)) && Number(value) > 0 && Number(value) <= 2147483647,
  );
  const savingRef = useRef(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const saveOptions = async (patch: ParserOptionsPatch) => {
    if (savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const response = await BApi.options.patchThirdPartyOptions(patch);

      if (response.code) throw new Error(response.message || t("postParser.result.failed"));
      useThirdPartyOptionsStore.getState().update(patch);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"xl"}
      title={t("postParser.action.configuration")}
      onDestroyed={props.onDestroyed}
    >
      <Tabs disableAnimation isVertical aria-label="Options" classNames={{ panel: "flex-1 w-0" }}>
        <Tab key="general" title={t("postParser.config.general")}>
          <div className="space-y-3">
            <Checkbox
              isDisabled={saving}
              isSelected={!!automaticallyParsing}
              onValueChange={(value) => saveOptions({ automaticallyParsingPosts: value })}
            >
              {t("postParser.label.automaticallyParsing")}
            </Checkbox>
            <p className="text-sm text-default-500">{t("postParser.config.automaticHint")}</p>
            <Input
              description={t("postParser.config.maxConcurrencyHint")}
              isDisabled={saving}
              label={t("postParser.config.maxConcurrency")}
              max={2147483647}
              min={1}
              step={1}
              type="number"
              value={limits.total}
              onValueChange={(value) => setLimits((previous) => ({ ...previous, total: value }))}
            />
            <Input
              description={t("postParser.config.aiMaxConcurrencyHint")}
              isDisabled={saving}
              label={t("postParser.config.aiMaxConcurrency")}
              max={2147483647}
              min={1}
              step={1}
              type="number"
              value={limits.ai}
              onValueChange={(value) => setLimits((previous) => ({ ...previous, ai: value }))}
            />
            <p className="text-sm text-default-500">{t("postParser.config.siteConcurrencyHint")}</p>
            <Button
              color="primary"
              isDisabled={saving || !validLimits}
              isLoading={saving}
              size="sm"
              onPress={() =>
                saveOptions({
                  postParserMaxConcurrency: Number(limits.total),
                  postParserAiMaxConcurrency: Number(limits.ai),
                })
              }
            >
              {t("postParser.config.saveConcurrency")}
            </Button>
            {error && (
              <p className="text-sm text-danger" role="alert">
                {error}
              </p>
            )}
          </div>
        </Tab>
        <Tab key="ai" title={t<string>("postParser.config.ai")}>
          <div className="space-y-4">
            <AiProviderPanel />
            <AiFeaturePanel features={[AiFeature.Default, AiFeature.PostParser]} />
          </div>
        </Tab>
        <Tab key="soulplus" title="SoulPlus">
          <SoulPlusConfigPanel fields={[SoulPlusConfigField.Accounts, SoulPlusConfigField.Other]} />
        </Tab>
      </Tabs>
    </Modal>
  );
};

ConfigurationModal.displayName = "ConfigurationModal";

export default ConfigurationModal;
