import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import dayjs from 'dayjs';
import { ButtonGroup, Textarea } from '@heroui/react';
import _ from 'lodash';
import ExHentai from './ExHentai';
import {
  bilibiliDownloadTaskTypes,
  exHentaiDownloadTaskTypes,
  pixivDownloadTaskTypes,
  ThirdPartyId,
  thirdPartyIds,
} from '@/sdk/constants';

import { Button, Checkbox, Modal } from '@/components/bakaui';
import type { components } from '@/sdk/BApi2';
import type { DestroyableProps } from '@/components/bakaui/types';
import ThirdPartyIcon from '@/components/ThirdPartyIcon';
import Pixiv from '@/pages/Downloader/components/TaskDetailModal/Pixiv';
import Bilibili from '@/pages/Downloader/components/TaskDetailModal/Bilibili';
import DurationInput from '@/components/DurationInput';
import type { LabelValue } from '@/components/types';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const ThirdPartyIdTaskTypesMap: { [key in ThirdPartyId]?: LabelValue[] } = {
  [ThirdPartyId.Bilibili]: bilibiliDownloadTaskTypes,
  [ThirdPartyId.ExHentai]: exHentaiDownloadTaskTypes,
  [ThirdPartyId.Pixiv]: pixivDownloadTaskTypes,
};

type Form = components['schemas']['Bakabase.InsideWorld.Models.RequestModels.DownloadTaskAddInputModel'];

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
        keyAndNames: { [task!.key]: task!.name },
      };
      setForm(form);
    }
  };

  useEffect(() => {
    init();
  }, []);

  const validateForm = (form: Partial<Form>): boolean => {
    if (form.thirdPartyId && form.downloadPath && _.keys(form.keyAndNames).length > 0 && form.type) {
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
            type={form.type}
            isReadOnly={!isAdding}
            onChange={v => {
              setForm({ ...v });
            }}
            form={form}
          />,
        );
        break;
      case ThirdPartyId.Pixiv: {
        items.push(
          <Pixiv
            form={form}
            type={form.type}
            isReadOnly={!isAdding}
            onChange={v => {
              setForm({ ...v });
            }}
          />,
        );
        break;
      }
      case ThirdPartyId.Bilibili: {
        items.push(
          <Bilibili
            type={form.type}
            form={form}
            isReadOnly={!isAdding}
            onChange={v => {
              setForm({ ...v });
            }}
          />,
        );
      }
    }

    items.push(
      <>
        <div>{t('Duration')}</div>
        <div className={'w-[200px]'}>
          <DurationInput
            // className={'col-span-2'}
            duration={form.interval == undefined ? undefined : dayjs.duration({ seconds: form.interval })}
            onDurationChange={d => {
              setForm({
                ...form,
                interval: d.asSeconds(),
              });
            }}
          />
        </div>
      </>,
    );

    items.push(
      <>
        <div>{t('Checkpoint')}</div>
        <Textarea
          size={'sm'}
          // className={'col-span-2'}
          // label={t('Checkpoint')}
          description={(
            <div>
              <div>{t('You can set the previous downloading checkpoint manually to make the downloader start the downloading task from it.')}</div>
              <div>{t('In most cases, you should let this field set by downloader automatically.')}</div>
              <div>{t('Each downloader has its own checkpoint format, and invalid checkpoint data will be ignored. You can find samples on our online document.')}</div>
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
    //       {t('Check')}
    //     </Button>
    //   ),
    // });

    items.push(
      <>
        <div>{t('Auto retry')}</div>
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
            <span className={'opacity-60 text-xs'}>{t('Retry automatically when the downloading task failed.')}</span>
          </div>
        </div>
      </>,
    );

    if (isAdding) {
      items.push(
        <>
          <div>{t('Allow duplicate submission')}</div>
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
              <span className={'opacity-60 text-xs'}>{t('In general, it\'s not necessary to create identical tasks.')}</span>
            </div>
          </div>
        </>,
      );
    }

    // console.log(form);

    return (
      <>
        {items}
      </>
    );
  };

  console.log(form);

  return (
    <>
      <Modal
        onDestroyed={onDestroyed}
        size={'xl'}
        defaultVisible
        title={isAdding ? t('Creating download task') : t('Download task')}
        className={'download-task-detail'}
        onOk={async () => {
          const validForm = form as Form;
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
        footer={{
          actions: ['ok', 'cancel'],
          okProps: {
            isDisabled: !validateForm(form),
          },
        }}
      >
        <div className={'grid gap-2 items-center'} style={{ gridTemplateColumns: 'auto 1fr' }}>
          <div>{t('Site')}</div>
          <div>
            <ButtonGroup size={'sm'}>
              {thirdPartyIds.filter(x => x.value in ThirdPartyIdTaskTypesMap).map((tpId) => {
                const isSelected = form.thirdPartyId == tpId.value;

                return (
                  <Button
                    onPress={() => {
                      if (form.thirdPartyId != tpId.value) {
                        setForm({
                          thirdPartyId: tpId.value,
                        });
                      }
                    }}
                    color={isSelected ? 'primary' : undefined}
                    isDisabled={!isAdding}
                  >
                    <ThirdPartyIcon thirdPartyId={tpId.value} />
                    {t(tpId.label)}
                  </Button>
                );
              })}
            </ButtonGroup>
          </div>
          {form.thirdPartyId && (
            <>
              <div>
                {t('Task type')}
              </div>
              <div>
                <ButtonGroup size={'sm'}>
                  {ThirdPartyIdTaskTypesMap[form.thirdPartyId]?.map((type) => {
                    const isSelected = form.type == type.value;
                    return (
                      <Button
                        key={type.value}
                        onPress={() => {
                          if (!isSelected) {
                            setForm({
                              thirdPartyId: form.thirdPartyId,
                              type: type.value,
                            });
                          }
                        }}
                        color={isSelected ? 'primary' : undefined}
                        isDisabled={!isAdding}
                      >
                        {type.label}
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
