"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { ThirdPartyId } from "@/sdk/constants";
import type {
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition,
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions,
} from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import DownloaderOptions from "./DownloaderOptions";

import { Modal, Tab, Tabs } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import { ThirdPartyId as ThirdPartyIdEnum } from "@/sdk/constants";

type Props = {
  onSubmitted?: any;
} & DestroyableProps;

const ConfigurationsModal = ({ onSubmitted, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [thirdPartyGroups, setThirdPartyGroups] = useState<{
    [key in ThirdPartyId]?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition[];
  }>({});
  const [downloaderOptions, setDownloaderOptions] = useState<{
    [key in ThirdPartyId]?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions;
  }>({});
  const [tmpOptions, setTmpOptions] = useState<{
    [key in ThirdPartyId]?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions;
  }>({});

  const [allNamingDefinitions, setAllNamingDefinitions] = useState<
    Partial<
      Record<
        ThirdPartyId,
        BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition
      >
    >
  >({});

  const [selectedTab, setSelectedTab] = useState<ThirdPartyId | "">("");

  // Load options for a specific third party
  const loadOptionsForThirdParty = async (thirdPartyId: ThirdPartyId) => {
    try {
      // Use the first definition for this third party to load options
      const definitions = thirdPartyGroups[thirdPartyId];

      if (!definitions || definitions.length === 0) {
        return;
      }

      const firstDefinition = definitions[0];
      const optionsRes = await BApi.downloadTask.getDownloaderOptions(
        thirdPartyId,
        { taskType: firstDefinition.taskType },
      );

      if (optionsRes.data) {
        setDownloaderOptions((prev) => ({
          ...prev,
          [thirdPartyId]: optionsRes.data!,
        }));
      }
    } catch (error) {
      console.error(
        `Failed to reload options for third party ${thirdPartyId}:`,
        error,
      );
    }
  };

  // Load downloader definitions and options
  useEffect(() => {
    const loadData = async () => {
      try {
        // Get all downloader definitions
        const definitionsRes =
          await BApi.downloadTask.getAllDownloaderDefinitions();
        const definitions = definitionsRes.data || [];

        // Group definitions by third party ID
        const groups: {
          [key in ThirdPartyId]?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition[];
        } = {};
        const namingMap: Partial<
          Record<
            ThirdPartyId,
            BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition
          >
        > = {};

        definitions.forEach((def) => {
          if (!groups[def.thirdPartyId]) {
            groups[def.thirdPartyId] = [];
          }
          groups[def.thirdPartyId] ??= [];
          groups[def.thirdPartyId]!.push(def);

          // Use the first definition for naming
          if (!namingMap[def.thirdPartyId]) {
            namingMap[def.thirdPartyId] = def;
          }
        });

        setThirdPartyGroups(groups);
        setAllNamingDefinitions(namingMap);

        // Set initial selected tab to first third party
        const thirdPartyIds = Object.keys(groups).map(
          (id) => parseInt(id, 10) as ThirdPartyId,
        );

        if (thirdPartyIds.length > 0 && !selectedTab) {
          setSelectedTab(thirdPartyIds[0]);
        }

        // Get options for each third party (using first definition)
        const optionsPromises = thirdPartyIds.map(async (thirdPartyId) => {
          try {
            const firstDefinition = groups[thirdPartyId]?.[0];

            if (!firstDefinition) {
              return null;
            }

            const optionsRes = await BApi.downloadTask.getDownloaderOptions(
              thirdPartyId,
              { taskType: firstDefinition.taskType },
            );

            return {
              thirdPartyId,
              options: optionsRes.data,
            };
          } catch (error) {
            console.error(
              `Failed to load options for third party ${thirdPartyId}:`,
              error,
            );

            return null;
          }
        });

        const optionsResults = await Promise.all(optionsPromises);
        const optionsMap: {
          [key in ThirdPartyId]?: BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions;
        } = {};

        optionsResults.forEach((result) => {
          if (result && result.options) {
            optionsMap[result.thirdPartyId] = result.options;
          }
        });
        setDownloaderOptions(optionsMap);
      } catch (error) {
        console.error("Failed to load downloader data:", error);
      }
    };

    loadData();
  }, []);

  const handleSubmit = async () => {
    try {
      const updatePromises = Object.entries(tmpOptions)
        .filter(([, options]) => options && Object.keys(options).length > 0)
        .map(async ([thirdPartyIdStr, patchOptions]) => {
          const thirdPartyId = parseInt(thirdPartyIdStr, 10) as ThirdPartyId;
          const firstDefinition = thirdPartyGroups[thirdPartyId]?.[0];

          if (!firstDefinition) {
            return;
          }

          const currentOptions = downloaderOptions[thirdPartyId];
          const updatedOptions = {
            ...currentOptions,
            ...patchOptions,
          };

          return BApi.downloadTask.putDownloaderOptions(
            thirdPartyId,
            updatedOptions,
            { taskType: firstDefinition.taskType },
          );
        });

      await Promise.all(updatePromises);
      onSubmitted?.();
    } catch (error) {
      console.error("Failed to save downloader options:", error);
    }
  };

  // Handle tab change and reload options
  const handleTabChange = async (tabKey: string) => {
    const thirdPartyId = parseInt(tabKey, 10) as ThirdPartyId;

    setSelectedTab(thirdPartyId);
    await loadOptionsForThirdParty(thirdPartyId);
  };

  return (
    <Modal
      defaultVisible
      size={"xl"}
      title={t<string>("Configurations")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <Tabs
        selectedKey={selectedTab?.toString()}
        onSelectionChange={(key) => handleTabChange(key as string)}
      >
        {Object.entries(thirdPartyGroups).map(([thirdPartyIdStr]) => {
          const thirdPartyId = parseInt(thirdPartyIdStr, 10) as ThirdPartyId;
          const options = downloaderOptions[thirdPartyId];
          const mergedOptions = { ...options, ...tmpOptions[thirdPartyId] };
          const isDeveloping = isThirdPartyDeveloping(thirdPartyId);

          // Get third party name from enum
          const thirdPartyName =
            ThirdPartyIdEnum[thirdPartyId] || `Third Party ${thirdPartyId}`;

          return (
            <Tab
              key={thirdPartyId}
              title={
                <div className="flex items-center gap-2">
                  <ThirdPartyIcon thirdPartyId={thirdPartyId} />
                  {t<string>(thirdPartyName)}
                  {isDeveloping && <DevelopingChip size="sm" />}
                </div>
              }
            >
              {options && (
                <DownloaderOptions
                  namingDefinition={allNamingDefinitions[thirdPartyId]}
                  options={mergedOptions}
                  thirdPartyId={thirdPartyId}
                  onChange={(patchOptions) => {
                    setTmpOptions({
                      ...tmpOptions,
                      [thirdPartyId]: {
                        ...tmpOptions[thirdPartyId],
                        ...patchOptions,
                      },
                    });
                  }}
                />
              )}
            </Tab>
          );
        })}
      </Tabs>
    </Modal>
  );
};

export default ConfigurationsModal;
