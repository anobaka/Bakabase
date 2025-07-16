"use client";

import type { components } from "@/sdk/BApi2";
import type { DestroyableProps } from "@/components/bakaui/types";
import type { LabelValue } from "@/components/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import dayjs from "dayjs";
import { ButtonGroup, Textarea } from "@heroui/react";
import _ from "lodash";

import ExHentai from "./ExHentai";

import {
  bilibiliDownloadTaskTypes,
  exHentaiDownloadTaskTypes,
  pixivDownloadTaskTypes,
  ThirdPartyId,
  thirdPartyIds,
} from "@/sdk/constants";
import { Button, Checkbox, Modal } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import Pixiv from "@/pages/downloader/components/TaskDetailModal/Pixiv";
import Bilibili from "@/pages/downloader/components/TaskDetailModal/Bilibili";
import DurationInput from "@/components/DurationInput";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const ThirdPartyIdTaskTypesMap: { [key in ThirdPartyId]?: LabelValue[] } = {
  [ThirdPartyId.Bilibili]: bilibiliDownloadTaskTypes,
  [ThirdPartyId.ExHentai]: exHentaiDownloadTaskTypes,
  [ThirdPartyId.Pixiv]: pixivDownloadTaskTypes,
};

type Form =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.Downloader.Models.Input.DownloadTaskAddInputModel"];

type Props = {
  id?: number;
} & DestroyableProps;

export default ({
  // onCreatedOrUpdated,
  // onClose,
  onDestroyed,
  id,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const isAdding = !(id && id > 0);
  const [form, setForm] = useState<Partial<Form>>({ autoRetry: true });

  console.log(id);

  const init = async () => {
    if (!isAdding) {
      const task = (await BApi.downloadTask.getDownloadTask(id)).data;
      const form: Partial<Form> = {
        ...task,
        keyAndNames: { [task!.key]: task!.name || null },
      };

      setForm(form);
    }
  };

  useEffect(() => {
    init();
  }, []);

  const validateForm = (form: Partial<Form>): boolean => {
    console.log(form);
    if (
      form.thirdPartyId &&
      form.downloadPath &&
      _.keys(form.keyAndNames).length > 0 &&
      form.type
    ) {
      return true;
    }

    return false;
  };

  const renderFormItems = () => {
    if (!form.thirdPartyId || !form.type) {
      return;
    }

    const items: any[] = [];

    switch (form.thirdPartyId) {
      case ThirdPartyId.ExHentai:
        items.push(
          <ExHentai
            form={form}
            isReadOnly={!isAdding}
            type={form.type}
            onChange={(v) => {
              setForm({ ...form, ...v });
            }}
          />,
        );
        break;
      case ThirdPartyId.Pixiv: {
        items.push(
          <Pixiv
            form={form}
            isReadOnly={!isAdding}
            type={form.type}
            onChange={(v) => {
              setForm({ ...form, ...v });
            }}
          />,
        );
        break;
      }
      case ThirdPartyId.Bilibili: {
        items.push(
          <Bilibili
            form={form}
            isReadOnly={!isAdding}
            type={form.type}
            onChange={(v) => {
              setForm({ ...form, ...v });
            }}
          />,
        );
      }
    }

    items.push(
      <>
        <div>{t<string>("Duration")}</div>
        <div className={"w-[200px]"}>
          <DurationInput
            // className={'col-span-2'}
            duration={
              form.interval == undefined
                ? undefined
                : dayjs.duration({ seconds: form.interval })
            }
            onDurationChange={(duration) => {
              setForm({
                ...form,
                interval: duration.asSeconds(),
              });
            }}
          />
        </div>
      </>,
    );

    items.push(
      <>
        <div>{t<string>("Checkpoint")}</div>
        <Textarea
          size={"sm"}
          // className={'col-span-2'}
          // label={t<string>('Checkpoint')}
          description={(
            <div>
              <div>{t<string>('You can set the previous downloading checkpoint manually to make the downloader start the downloading task from it.')}</div>
              <div>{t<string>('In most cases, you should let this field set by downloader automatically.')}</div>
              <div>{t<string>('Each downloader has its own checkpoint format, and invalid checkpoint data will be ignored. You can find samples on our online document.')}</div>
            </div>
          )}
          value={form.checkpoint}
          onValueChange={(v) => {
            setForm({
              ...form,
              checkpoint: v,
            });
          }}
        />
      </>,
    );

    // items.push({
    //   label: 'Global configurations',
    //   component: (
    //     <Button
    //       type={'primary'}
    //       text
    //       onClick={() => {
    //         setConfigurationsVisible(true);
    //       }}
    //     >
    //       {t<string>('Check')}
    //     </Button>
    //   ),
    // });

    items.push(
      <>
        <div>{t<string>("Auto retry")}</div>
        <div>
          <div>
            <Checkbox
              defaultSelected
              isSelected={form.autoRetry}
              onValueChange={(v) => {
                setForm({
                  ...form,
                  autoRetry: v,
                });
              }}
            />
            &nbsp;
            <span className={"opacity-60 text-xs"}>
              {t<string>(
                "Retry automatically when the downloading task failed.",
              )}
            </span>
          </div>
        </div>
      </>,
    );

    if (isAdding) {
      items.push(
        <>
          <div>{t<string>("Allow duplicate submission")}</div>
          <div>
            <div>
              <Checkbox
                isSelected={form.isDuplicateAllowed}
                onValueChange={(v) => {
                  setForm({
                    ...form,
                    isDuplicateAllowed: v,
                  });
                }}
              />
              &nbsp;
              <span className={"opacity-60 text-xs"}>
                {t<string>(
                  "In general, it's not necessary to create identical tasks.",
                )}
              </span>
            </div>
          </div>
        </>,
      );
    }

    // console.log(form);

    return <>{items}</>;
  };

  console.log(form);

  return (
    <>
      <Modal
        defaultVisible
        className={"download-task-detail"}
        footer={{
          actions: ["ok", "cancel"],
          okProps: {
            isDisabled: !validateForm(form),
          },
        }}
        size={"xl"}
        title={
          isAdding
            ? t<string>("Creating download task")
            : t<string>("Download task")
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
          <div>{t<string>("Site")}</div>
          <div>
            <ButtonGroup size={"sm"}>
              {thirdPartyIds
                .filter((x) => x.value in ThirdPartyIdTaskTypesMap)
                .map((tpId) => {
                  const isSelected = form.thirdPartyId == tpId.value;

                  return (
                    <Button
                      color={isSelected ? "primary" : undefined}
                      isDisabled={!isAdding}
                      onPress={() => {
                        if (form.thirdPartyId != tpId.value) {
                          setForm({
                            thirdPartyId: tpId.value,
                          });
                        }
                      }}
                    >
                      <ThirdPartyIcon thirdPartyId={tpId.value} />
                      {t<string>(tpId.label)}
                    </Button>
                  );
                })}
            </ButtonGroup>
          </div>
          {form.thirdPartyId && (
            <>
              <div>{t<string>("Task type")}</div>
              <div>
                <ButtonGroup size={"sm"}>
                  {ThirdPartyIdTaskTypesMap[form.thirdPartyId]?.map((type) => {
                    const isSelected = form.type == type.value;

                    return (
                      <Button
                        key={type.value}
                        color={isSelected ? "primary" : undefined}
                        isDisabled={!isAdding}
                        onPress={() => {
                          if (!isSelected) {
                            setForm({
                              thirdPartyId: form.thirdPartyId,
                              type: type.value,
                            });
                          }
                        }}
                      >
                        {t(type.label)}
                      </Button>
                    );
                  })}
                </ButtonGroup>
              </div>
            </>
          )}
          {renderFormItems()}
        </div>
      </Modal>
    </>
  );
};
