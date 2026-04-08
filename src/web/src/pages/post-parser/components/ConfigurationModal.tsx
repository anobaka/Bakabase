"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";

import { Modal, Tab, Tabs } from "@/components/bakaui";
import { AiFeature } from "@/sdk/constants";
import { SoulPlusConfigPanel, SoulPlusConfigField } from "@/components/ThirdPartyConfig/platforms/SoulPlusConfig";
import AiProviderPanel from "@/components/AiProviderPanel";
import AiFeaturePanel from "@/components/AiFeaturePanel";

type Props = DestroyableProps;
const ConfigurationModal = (props: Props) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"xl"}
      onDestroyed={props.onDestroyed}
    >
      <Tabs isVertical aria-label="Options" classNames={{ panel: "flex-1 w-0" }}>
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
