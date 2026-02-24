"use client";

import type { components } from "@/sdk/BApi2";
import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition,
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions,
} from "@/sdk/Api";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ButtonGroup, Input, Textarea } from "@heroui/react";

import { ThirdPartyId } from "@/sdk/constants";
import { Alert, Button, Modal } from "@/components/bakaui";
import NavigateButton from "@/components/NavigateButton";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import {
  DownloadTaskFieldMap,
  DownloadTaskFieldType,
  DownloadTaskTypeIconMap,
} from "@/pages/downloader/components/TaskDetailModal/models.ts";
import LuxRequired from "@/pages/downloader/components/TaskDetailModal/components/LuxRequired.tsx";
import FfMpegRequired from "@/pages/downloader/components/TaskDetailModal/components/FfMpegRequired.tsx";
import PageRange from "@/pages/downloader/components/TaskDetailModal/components/PageRange.tsx";
import BilibiliFavoritesSelector from "@/pages/downloader/components/TaskDetailModal/components/BilibiliFavoritesSelector.tsx";
import DownloadPathSelectorField from "@/pages/downloader/components/TaskDetailModal/components/DownloadPathSelectorField.tsx";
import IntervalField from "@/pages/downloader/components/TaskDetailModal/components/IntervalField.tsx";
import CheckpointField from "@/pages/downloader/components/TaskDetailModal/components/CheckpointField.tsx";
import AutoRetryField from "@/pages/downloader/components/TaskDetailModal/components/AutoRetryField.tsx";
import AllowDuplicateField from "@/pages/downloader/components/TaskDetailModal/components/AllowDuplicateField.tsx";
import PreferTorrentField from "@/pages/downloader/components/TaskDetailModal/components/PreferTorrentField.tsx";

type Form =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input.DownloadTaskAddInputModel"];

type Props = {
  id?: number;
} & DestroyableProps;

const DownloadTaskDetailModal = ({ onDestroyed, id }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const isAdding = !(id && id > 0);
  const [form, setForm] = useState<Partial<Form>>({ autoRetry: true });
  const [taskDefinitions, setTaskDefinitions] = useState<
    BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition[]
  >([]);
  const [downloaderOptions, setDownloaderOptions] =
    useState<BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions | null>(
      null,
    );

  const init = async () => {
    if (!isAdding) {
      const task = (await BApi.downloadTask.getDownloadTask(id)).data!;
      const form: Partial<Form> = {
        ...task,
        keys: [task.key],
        names: task.name === undefined ? [] : [task.name],
      };

      setForm(form);
    }

    const taskDefinitionsResponse =
      await BApi.downloadTask.getAllDownloaderDefinitions();

    setTaskDefinitions(taskDefinitionsResponse.data || []);
  };

  useEffect(() => {
    init();
  }, []);

  // Load downloader options when thirdPartyId and type change
  useEffect(() => {
    const loadDownloaderOptions = async () => {
      if (form.thirdPartyId && form.type) {
        try {
          const optionsRes = await BApi.downloadTask.getDownloaderOptions(
            form.thirdPartyId,
            { taskType: form.type },
          );

          const options = optionsRes.data;

          setDownloaderOptions(options || null);

          // If options has defaultPath and form doesn't have downloadPath, set it
          if (options?.defaultPath && !form.downloadPath) {
            setForm((prev) => ({
              ...prev,
              downloadPath: options.defaultPath,
            }));
          }
        } catch (error) {
          console.error("Failed to load downloader options:", error);
          setDownloaderOptions(null);
        }
      } else {
        setDownloaderOptions(null);
      }
    };

    loadDownloaderOptions();
  }, [form.thirdPartyId, form.type]);

  // Get unique third party IDs from task definitions
  const availableThirdParties = React.useMemo(() => {
    const uniqueThirdParties = new Map<ThirdPartyId, string>();

    taskDefinitions.forEach((def) => {
      if (!uniqueThirdParties.has(def.thirdPartyId)) {
        // Use ThirdPartyId enum to get the name, or fallback to the definition name
        const thirdPartyName = ThirdPartyId[def.thirdPartyId] || def.name;

        uniqueThirdParties.set(def.thirdPartyId, thirdPartyName);
      }
    });

    return Array.from(uniqueThirdParties.entries())
      .map(([id, name]) => ({
        id,
        name,
      }))
      .sort((a, b) => a.id - b.id);
  }, [taskDefinitions]);

  // Get task types for the selected third party
  const availableTaskTypes = React.useMemo(() => {
    if (!form.thirdPartyId) return [];

    return taskDefinitions
      .filter((def) => def.thirdPartyId === form.thirdPartyId)
      .map((def) => ({
        value: def.taskType,
        label: def.name,
        description: def.description,
      }));
  }, [taskDefinitions, form.thirdPartyId]);

  const validateForm = (form: Partial<Form>): boolean => {
    if (
      form.thirdPartyId &&
      form.downloadPath &&
      form.keys &&
      form.keys.length > 0 &&
      form.type
    ) {
      return true;
    }

    return false;
  };

  const renderFields = () => {
    if (form.thirdPartyId && form.type) {
      const fields = DownloadTaskFieldMap[form.thirdPartyId]?.[form.type] ?? [];

      return fields.map((f) => {
        switch (f.type) {
          case DownloadTaskFieldType.Key:
            if (!form.keys || !form.keys.length) {
              if (f.defaultValue) {
                form.keys = [f.defaultValue];
              }
            }

            return (
              <Input
                defaultValue={f.defaultValue}
                label={f.label && t(f.label)}
                placeholder={f.placeholder}
                size={"sm"}
                value={form.keys?.[0]}
                onValueChange={(v) => {
                  setForm({
                    ...form,
                    keys: [v],
                  });
                }}
              />
            );
          case DownloadTaskFieldType.Keys:
            return (
              <Textarea
                label={f.label && t(f.label)}
                placeholder={f.placeholder}
                size={"sm"}
                value={form.keys?.join("\n")}
                onValueChange={(v) => {
                  setForm({
                    ...form,
                    keys: v
                      .split("\n")
                      .map((k) => k.trim())
                      .filter((k) => k),
                  });
                }}
              />
            );
          case DownloadTaskFieldType.BilibiliFavorites:
            const key = form.keys?.[0];
            const numberKey = key ? parseInt(key, 10) : undefined;

            return (
              <BilibiliFavoritesSelector
                isDisabled={!isAdding}
                value={numberKey}
                onChange={(v) =>
                  setForm({
                    ...form,
                    keys: [v.toString()],
                  })
                }
              />
            );
          case DownloadTaskFieldType.PageRange:
            return (
              <PageRange
                end={form.endPage}
                start={form.startPage}
                onChange={(start, end) =>
                  setForm({
                    ...form,
                    startPage: start,
                    endPage: end,
                  })
                }
              />
            );
          case DownloadTaskFieldType.FfMpegRequired:
            return <FfMpegRequired />;
          case DownloadTaskFieldType.LuxRequired:
            return <LuxRequired />;
          case DownloadTaskFieldType.DownloadPath:
            return (
              <DownloadPathSelectorField
                downloadPath={form.downloadPath}
                onChange={(downloadPath) => {
                  setForm({
                    ...form,
                    downloadPath,
                  });
                }}
              />
            );
          case DownloadTaskFieldType.CheckInterval:
            return (
              <IntervalField
                interval={form.interval}
                onChange={(interval) => {
                  setForm({
                    ...form,
                    interval,
                  });
                }}
              />
            );
          case DownloadTaskFieldType.Checkpoint:
            return (
              <CheckpointField
                checkpoint={form.checkpoint}
                onChange={(checkpoint) => {
                  setForm({
                    ...form,
                    checkpoint,
                  });
                }}
              />
            );
          case DownloadTaskFieldType.AutoRetry:
            return (
              <AutoRetryField
                autoRetry={form.autoRetry}
                onChange={(autoRetry) => {
                  setForm({
                    ...form,
                    autoRetry,
                  });
                }}
              />
            );
          case DownloadTaskFieldType.AllowDuplicate:
            return isAdding ? (
              <AllowDuplicateField
                isDuplicateAllowed={form.isDuplicateAllowed}
                onChange={(isDuplicateAllowed) => {
                  setForm({
                    ...form,
                    isDuplicateAllowed,
                  });
                }}
              />
            ) : null;
          case DownloadTaskFieldType.PreferTorrent: {
            const parsedOptions = form.options ? JSON.parse(form.options) : {};
            if (parsedOptions.preferTorrent === undefined) {
              parsedOptions.preferTorrent = true;
              form.options = JSON.stringify(parsedOptions);
            }
            const preferTorrent = parsedOptions.preferTorrent;

            return (
              <PreferTorrentField
                preferTorrent={preferTorrent}
                onChange={(value) => {
                  const newOptions = { ...parsedOptions, preferTorrent: value };
                  setForm({
                    ...form,
                    options: JSON.stringify(newOptions),
                  });
                }}
              />
            );
          }
        }
      });
    }
  };

  return (
    <>
      <Modal
        defaultVisible
        footer={{
          actions: ["ok", "cancel"],
          okProps: {
            isDisabled: !validateForm(form),
          },
        }}
        size={"xl"}
        title={
          isAdding
            ? t<string>("downloader.modal.creatingTask")
            : t<string>("downloader.modal.taskDetail")
        }
        onDestroyed={onDestroyed}
        onOk={async () => {
          const validForm = form as Form;

          console.log(form, validForm);

          if (isAdding) {
            const r = await BApi.downloadTask.addDownloadTask(validForm);

            if (r.code) {
              throw new Error(r.message);
            }
          } else {
            const r = await BApi.downloadTask.putDownloadTask(id, validForm);

            if (r.code) {
              throw new Error(r.message);
            }
          }
        }}
      >
        <div
          className={"grid gap-2 items-center"}
          style={{ gridTemplateColumns: "auto 1fr" }}
        >
          <div>{t<string>("downloader.label.site")}</div>
          <div>
            <ButtonGroup size={"sm"}>
              {availableThirdParties.map((thirdParty) => {
                const isSelected = form.thirdPartyId === thirdParty.id;
                const isDeveloping = isThirdPartyDeveloping(thirdParty.id);

                return (
                  <Button
                    color={isSelected ? "primary" : undefined}
                    isDisabled={!isAdding}
                    onPress={() => {
                      if (form.thirdPartyId !== thirdParty.id) {
                        setForm({
                          thirdPartyId: thirdParty.id,
                          type: undefined, // Reset task type when changing third party
                        });
                      }
                    }}
                  >
                    <ThirdPartyIcon thirdPartyId={thirdParty.id} />
                    {t<string>(thirdParty.name)}
                    {isDeveloping && <DevelopingChip size="sm" />}
                  </Button>
                );
              })}
            </ButtonGroup>
          </div>
          {form.thirdPartyId &&
            (availableTaskTypes.length > 0 ? (
              <>
                <div>{t<string>("downloader.label.taskType")}</div>
                <div>
                  <ButtonGroup size={"sm"}>
                    {availableTaskTypes.map((taskType) => {
                      const isSelected = form.type === taskType.value;
                      const Icon = DownloadTaskTypeIconMap[form.thirdPartyId!]?.[taskType.value];

                      return (
                        <Button
                          key={taskType.value}
                          color={isSelected ? "primary" : undefined}
                          isDisabled={!isAdding}
                          title={taskType.description}
                          onPress={() => {
                            if (!isSelected) {
                              setForm({
                                ...form,
                                type: taskType.value,
                                keys: undefined,
                              });
                            }
                          }}
                        >
                          {Icon && <Icon className="text-base" />}
                          {t(taskType.label)}
                        </Button>
                      );
                    })}
                  </ButtonGroup>
                </div>
              </>
            ) : (
              t<string>("downloader.status.notAvailable")
            ))}
        </div>
        {form.thirdPartyId === ThirdPartyId.ExHentai && (
          <Alert
            color="primary"
            variant="flat"
            description={
              <div className="flex items-center gap-1 flex-wrap">
                <span>{t<string>("downloader.tip.exHentaiUserscript")}</span>
                <NavigateButton to="/third-party-integration">
                  {t<string>("downloader.tip.goToThirdPartyIntegration")}
                </NavigateButton>
              </div>
            }
          />
        )}
        {form.thirdPartyId && form.type && (
          <div className={"flex flex-col gap-2"}>{renderFields()}</div>
        )}
      </Modal>
    </>
  );
};

export default DownloadTaskDetailModal;
