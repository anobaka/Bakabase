"use client";

import type { components } from "@/sdk/BApi2";
import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloadTask,
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition,
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderOptions,
} from "@/sdk/Api";

import React, { forwardRef, useEffect, useId, useImperativeHandle, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { ButtonGroup, Input, Textarea } from "@heroui/react";
import moment from "moment";

import { DownloadTaskActionOnConflict, ThirdPartyId } from "@/sdk/constants";
import { Alert, Button, Modal } from "@/components/bakaui";
import { useDownloaderGlobalOptionsStore, useExHentaiOptionsStore } from "@/stores/options";
import NavigateButton from "@/components/NavigateButton";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";
import DevelopingChip from "@/components/Chips/DevelopingChip";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import BApi from "@/sdk/BApi";
import {
  DownloadTaskFieldMap,
  DownloadTaskFieldType,
  DownloadTaskTypeIconMap,
} from "@/pages/downloader/components/TaskDetailModal/models.ts";
import FfMpegRequired from "@/pages/downloader/components/TaskDetailModal/components/FfMpegRequired.tsx";
import PageRange from "@/pages/downloader/components/TaskDetailModal/components/PageRange.tsx";
import BilibiliFavoritesSelector from "@/pages/downloader/components/TaskDetailModal/components/BilibiliFavoritesSelector.tsx";
import DownloadPathSelectorField from "@/pages/downloader/components/TaskDetailModal/components/DownloadPathSelectorField.tsx";
import IntervalField from "@/pages/downloader/components/TaskDetailModal/components/IntervalField.tsx";
import CheckpointField from "@/pages/downloader/components/TaskDetailModal/components/CheckpointField.tsx";
import AutoRetryField from "@/pages/downloader/components/TaskDetailModal/components/AutoRetryField.tsx";
import AllowDuplicateField from "@/pages/downloader/components/TaskDetailModal/components/AllowDuplicateField.tsx";
import ExHentaiDownloadModeField from "./components/ExHentaiDownloadModeField";
import { parseExHentaiDownloadOptions } from "@/pages/downloader/exHentaiDownloadMode";
import DownloadResultsPanel from "@/pages/downloader/components/TaskDetailModal/components/DownloadResultsPanel";
import EstimatedRemainingTime from "@/pages/downloader/components/EstimatedRemainingTime";
import TaskLinkCopyButton from "@/pages/downloader/components/TaskLinkCopyButton";
import { useDownloadTasksStore } from "@/stores/downloadTasks";
import type { DownloadFormHandle, DownloadFormState } from "../downloadForm";
import { getEnumKey } from "@/i18n";

type Form =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Input.DownloadTaskAddInputModel"];
type SiteDefinition = Pick<
  BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition,
  "thirdPartyId" | "taskType" | "name" | "description"
>;

type Props = {
  id?: number;
} & DestroyableProps;

export const SiteDownloadForm = forwardRef<
  DownloadFormHandle,
  {
    id?: number;
    thirdPartyId?: ThirdPartyId;
    definitions?: SiteDefinition[];
    formId: string;
    onStateChange: (state: DownloadFormState) => void;
    onCreated?: () => void;
  }
>(({ id, thirdPartyId, definitions, formId, onStateChange, onCreated }, ref) => {
  const { t } = useTranslation();
  const autoStartAfterCreation = useDownloaderGlobalOptionsStore(
    (s) => s.data?.autoStartAfterCreation ?? false,
  );
  const exHentaiDefaults = useExHentaiOptionsStore((s) => s.data);

  const isAdding = !(id && id > 0);
  const [form, setForm] = useState<Partial<Form>>({ autoRetry: true, thirdPartyId });
  const [loadedTask, setLoadedTask] =
    useState<BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloadTask>();
  const liveTask = useDownloadTasksStore((s) => s.tasks.find((task) => task.id === id));
  const task = liveTask ?? loadedTask;
  const [taskDefinitions, setTaskDefinitions] = useState<SiteDefinition[]>(definitions ?? []);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [error, setError] = useState<string>();
  const [ready, setReady] = useState(isAdding && !!definitions);
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
      setLoadedTask(task);
    }

    if (!definitions) {
      const response = await BApi.downloadTask.getAllDownloaderDefinitions();
      if (response.code) throw new Error(response.message || t("downloadCenter.error.action"));
      setTaskDefinitions(response.data || []);
    }
    setReady(true);
  };

  useEffect(() => {
    void init().catch((e) =>
      setError(e instanceof Error ? e.message : t("downloadCenter.error.action")),
    );
  }, []);

  // Load downloader options when thirdPartyId and type change
  useEffect(() => {
    let active = true;
    const loadDownloaderOptions = async () => {
      if (form.thirdPartyId && form.type) {
        try {
          const optionsRes = await BApi.downloadTask.getDownloaderOptions(form.thirdPartyId, {
            taskType: form.type,
          });

          const options = optionsRes.data;

          if (!active) return;
          setDownloaderOptions(options || null);

          const defaultPath = options?.effectiveDefaultPath ?? options?.defaultPath;

          if (isAdding && defaultPath) {
            // Preserve a task override even if it was entered while this request was pending.
            setForm((prev) => (prev.downloadPath ? prev : { ...prev, downloadPath: defaultPath }));
          }
        } catch (error) {
          if (!active) return;
          console.error("Failed to load downloader options:", error);
          setDownloaderOptions(null);
        }
      } else {
        setDownloaderOptions(null);
      }
    };

    loadDownloaderOptions();

    return () => {
      active = false;
    };
  }, [form.thirdPartyId, form.type]);

  // Get unique third party IDs from task definitions
  const availableThirdParties = React.useMemo(() => {
    const uniqueThirdParties = new Map<ThirdPartyId, string>();

    taskDefinitions.forEach((def) => {
      // Cloud creation belongs to the unified download form. Existing task details still show their site.
      if (
        isAdding &&
        [ThirdPartyId.BaiduNetdisk, ThirdPartyId.GoogleDrive, ThirdPartyId.Gofile].includes(
          def.thirdPartyId,
        )
      )
        return;
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
      form.keys.some((k) => k.trim()) &&
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
                endContent={<TaskLinkCopyButton value={form.keys?.[0]} />}
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
                endContent={<TaskLinkCopyButton value={form.keys?.join("\n")} />}
                label={f.label && t(f.label)}
                placeholder={f.placeholder}
                size={"sm"}
                value={form.keys?.join("\n")}
                onValueChange={(v) => {
                  setForm({
                    ...form,
                    keys: v.split("\n"),
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
          case DownloadTaskFieldType.DownloadMode: {
            const parsedOptions = parseExHentaiDownloadOptions(form.options);
            const effectiveOptions = isAdding
              ? { ...exHentaiDefaults, ...parsedOptions }
              : parsedOptions;

            return (
              <ExHentaiDownloadModeField
                options={effectiveOptions}
                isExistingTask={!isAdding}
                onChange={(patch) => {
                  setForm({ ...form, options: JSON.stringify({ ...parsedOptions, ...patch }) });
                }}
              />
            );
          }
        }
      });
    }
  };

  const valid = ready && validateForm(form);
  useEffect(() => onStateChange({ canSubmit: valid, busy }), [valid, busy, onStateChange]);
  const submit = async () => {
    if (!valid || busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const validForm = {
        ...form,
        keys: form.keys?.map((k) => k.trim()).filter(Boolean),
      } as Form;

      if (isAdding) {
        const r = await BApi.downloadTask.addDownloadTask(validForm);

        if (r.code) {
          throw new Error(r.message);
        }

        if (autoStartAfterCreation) {
          const newIds = (r.data ?? [])
            .map((task) => task.id)
            .filter((tid): tid is number => typeof tid === "number");

          if (newIds.length > 0) {
            await BApi.downloadTask.startDownloadTasks({
              ids: newIds,
              actionOnConflict: DownloadTaskActionOnConflict.Ignore,
            });
          }
        }
      } else {
        const r = await BApi.downloadTask.putDownloadTask(id, validForm);

        if (r.code) {
          throw new Error(r.message);
        }
      }
      onCreated?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : t("downloadCenter.error.action"));
      throw e;
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  useImperativeHandle(ref, () => ({ submit }));

  return (
    <form
      id={formId}
      onSubmit={(event) => {
        event.preventDefault();
        void submit().catch(() => undefined);
      }}
    >
      <fieldset disabled={busy} className="min-w-0 space-y-3">
        {!isAdding && task && (
          <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-default-500">
            <div className="flex items-center gap-1">
              <dt>{t<string>("downloader.label.createdAt")}</dt>
              <dd>
                <time dateTime={task.createdAt}>
                  {moment(task.createdAt).format("YYYY-MM-DD HH:mm:ss")}
                </time>
              </dd>
            </div>
            {task.completedAt && (
              <div className="flex items-center gap-1">
                <dt>{t<string>("downloader.label.completedAt")}</dt>
                <dd>
                  <time dateTime={task.completedAt}>
                    {moment(task.completedAt).format("YYYY-MM-DD HH:mm:ss")}
                  </time>
                </dd>
              </div>
            )}
          </dl>
        )}
        {!isAdding && task && <EstimatedRemainingTime task={task} />}
        <div className="grid min-w-0 grid-cols-[max-content_minmax(0,1fr)] items-center gap-2">
          {thirdPartyId == null && (
            <>
              <div className="whitespace-nowrap">{t<string>("downloader.label.site")}</div>
              <div className="min-w-0">
                <ButtonGroup size={"sm"} className="max-w-full flex-wrap justify-start gap-y-1">
                  {availableThirdParties.map((thirdParty) => {
                    const isSelected = form.thirdPartyId === thirdParty.id;
                    const isDeveloping = isThirdPartyDeveloping(thirdParty.id);

                    return (
                      <Button
                        key={thirdParty.id}
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
                        {t<string>(getEnumKey("ThirdPartyId", thirdParty.name))}
                        {isDeveloping && <DevelopingChip size="sm" />}
                      </Button>
                    );
                  })}
                </ButtonGroup>
              </div>
            </>
          )}
          {form.thirdPartyId &&
            (availableTaskTypes.length > 0 ? (
              <>
                <div className="whitespace-nowrap">{t<string>("downloader.label.taskType")}</div>
                <div className="min-w-0">
                  <ButtonGroup size={"sm"} className="max-w-full flex-wrap justify-start gap-y-1">
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
            description={
              <div className="flex items-center gap-1 flex-wrap">
                <span>{t<string>("downloader.tip.exHentaiUserscript")}</span>
                <NavigateButton to="/third-party-configuration">
                  {t<string>("downloader.tip.goToThirdPartyIntegration")}
                </NavigateButton>
              </div>
            }
            variant="flat"
          />
        )}
        {form.thirdPartyId && form.type && (
          <div className="flex min-w-0 flex-col gap-2">{renderFields()}</div>
        )}
        {!isAdding && id != null && form.thirdPartyId === ThirdPartyId.ExHentai && (
          <DownloadResultsPanel taskId={id} />
        )}
        {error && (
          <p role="alert" className="text-sm text-danger">
            {error}
          </p>
        )}
      </fieldset>
    </form>
  );
});
SiteDownloadForm.displayName = "SiteDownloadForm";

const DownloadTaskDetailModal = ({ onDestroyed, id }: Props) => {
  const { t } = useTranslation();
  const formId = useId();
  const form = useRef<DownloadFormHandle>(null);
  const [state, setState] = useState<DownloadFormState>({ canSubmit: false, busy: false });
  return (
    <Modal
      defaultVisible
      size="xl"
      title={t(id ? "downloader.modal.taskDetail" : "downloader.modal.creatingTask")}
      onDestroyed={onDestroyed}
      isDismissable={!state.busy}
      footer={{
        actions: ["ok", "cancel"],
        okProps: { isDisabled: !state.canSubmit || state.busy },
      }}
      onOk={() => form.current?.submit()}
    >
      <SiteDownloadForm ref={form} id={id} formId={formId} onStateChange={setState} />
    </Modal>
  );
};

export default DownloadTaskDetailModal;
