"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Checkbox, Modal, Tab, Tabs } from "@/components/bakaui";
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
const ConfigurationModal = (props: Props) => {
  const { t } = useTranslation();
  const automaticallyParsing = useThirdPartyOptionsStore(
    (state) => state.data.automaticallyParsingPosts,
  );
  const savingRef = useRef(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const saveAutomaticParsing = async (value: boolean) => {
    if (savingRef.current) return;
    savingRef.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const response = await BApi.options.patchThirdPartyOptions({
        automaticallyParsingPosts: value,
      });

      if (response.code) throw new Error(response.message || t("postParser.result.failed"));
      useThirdPartyOptionsStore.getState().update({ automaticallyParsingPosts: value });
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
              onValueChange={saveAutomaticParsing}
            >
              {t("postParser.label.automaticallyParsing")}
            </Checkbox>
            <p className="text-sm text-default-500">{t("postParser.config.automaticHint")}</p>
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
