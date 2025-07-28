"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";

import { Modal, NumberInput, Tab, Tabs, Textarea } from "@/components/bakaui";
import { useSoulPlusOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { EditableValue } from "@/components/EditableValue";

type Props = DestroyableProps;
const ConfigurationModal = (props: Props) => {
  const { t } = useTranslation();
  const soulPlusOptions = useSoulPlusOptionsStore((state) => state.data);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"xl"}
      onDestroyed={props.onDestroyed}
    >
      <Tabs isVertical aria-label="Options">
        <Tab key="soulplus" title={t<string>("SoulPlus")}>
          <div className={"flex flex-col gap-2"}>
            <EditableValue
              Editor={Textarea}
              Viewer={Textarea}
              description={t<string>(
                "Cookie is used to access SoulPlus posts.",
              )}
              label={t<string>("Cookie")}
              value={soulPlusOptions.cookie}
              onSubmit={async (v) => {
                await BApi.options.patchSoulPlusOptions({
                  cookie: v,
                });
              }}
            />
            <EditableValue
              Editor={NumberInput}
              Viewer={NumberInput}
              description={t<string>(
                "Items priced below this value will be bought automatically.",
              )}
              editorProps={{
                min: 0,
                formatOptions: { useGrouping: false },
                hideStepper: true,
              }}
              label={t<string>("Auto buy threshold")}
              value={soulPlusOptions.autoBuyThreshold}
              viewerProps={{
                formatOptions: { useGrouping: false },
                hideStepper: true,
              }}
              onSubmit={async (v) => {
                await BApi.options.patchSoulPlusOptions({
                  autoBuyThreshold: v,
                });
              }}
            />
          </div>
        </Tab>
      </Tabs>
    </Modal>
  );
};

ConfigurationModal.displayName = "ConfigurationModal";

export default ConfigurationModal;
