import i18n from 'i18next';
import { Balloon, Button, Checkbox, Input, List, Message, Radio, Select, Table } from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CustomIcon from '@/components/CustomIcon';
import Title from '@/components/Title';
import {
  PatchAppOptions,
  PatchExHentaiOptions,
  PatchResourceOptions,
  PatchThirdPartyOptions,
  PatchUIOptions,
  ValidateCookie,
} from '@/sdk/apis';
import {
  AdditionalCoverDiscoveringSource,
  additionalCoverDiscoveringSources,
  CloseBehavior,
  CookieValidatorTarget,
  coverSaveLocations,
  DependentComponentStatus,
  startupPages,
} from '@/sdk/constants';
import ConfirmationButton from '@/components/ConfirmationButton';
import store from '@/store';
import type { BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions } from '@/sdk/Api';
import { uuidv4 } from '@/components/utils';
import dependentComponentIds from '@/core/models/Constants/DependentComponentIds';

export default ({ applyPatches = () => {} }: {applyPatches: (API: any, patches: any) => void}) => {
  const { t } = useTranslation();

  const appOptions = store.useModelState('appOptions');
  const resourceOptions = store.useModelState('resourceOptions');
  const exhentaiOptions = store.useModelState('exHentaiOptions');
  const thirdPartyOptions = store.useModelState('thirdPartyOptions');
  const [validatingExHentaiCookie, setValidatingExHentaiCookie] = useState(false);
  const uiOptions = store.useModelState('uiOptions');

  const [simpleSearchEngines, setSimpleSearchEngines] = useState<BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions[]>([]);
  const [tmpExHentaiOptions, setTmpExHentaiOptions] = useState(exhentaiOptions || {});

  const ffmpegState = store.useModelState('dependentComponentContexts')?.find(d => d.id == dependentComponentIds.FFMpeg);

  useEffect(() => {
    setSimpleSearchEngines(JSON.parse(JSON.stringify(thirdPartyOptions.simpleSearchEngines || [])));
  }, [thirdPartyOptions.simpleSearchEngines]);

  useEffect(() => {
    console.log('new exhentai options', exhentaiOptions);
    setTmpExHentaiOptions(JSON.parse(JSON.stringify(exhentaiOptions || {})));
  }, [exhentaiOptions]);

  console.log('rerender', tmpExHentaiOptions, exhentaiOptions);

  const functionSettings = [
    {
      label: 'Try discovering cover from',
      renderCell: () => {
        return (
          <Checkbox.Group
            value={resourceOptions?.additionalCoverDiscoveringSources}
            onChange={(list) => {
              applyPatches(PatchResourceOptions, {
                additionalCoverDiscoveringSources: list.filter((s) => s),
              });
            }}
          >
            {additionalCoverDiscoveringSources.map((s) => {
              if (s.value == AdditionalCoverDiscoveringSource.Video) {
                if (ffmpegState?.status != DependentComponentStatus.Installed) {
                  return (
                    <Balloon.Tooltip
                      key={s.value}
                      trigger={(
                        <Checkbox value={s.value} disabled>{t(s.label)}</Checkbox>
                      )}
                      align={'t'}
                    >
                      {t('FFmpeg bin directory must be set to enable this options.')}
                    </Balloon.Tooltip>
                  );
                }
              }

              return (
                <Checkbox key={s.value} value={s.value}>{t(s.label)}</Checkbox>
              );
            })}
          </Checkbox.Group>
        );
      },
    },
    {
      label: 'Cover save location',
      renderCell: () => {
        return (
          <Radio.Group
            value={resourceOptions?.coverOptions?.saveLocation ?? undefined}
            onChange={(v) => {
              applyPatches(PatchResourceOptions, {
                coverOptions: {
                  ...(resourceOptions?.coverOptions ?? {}),
                  saveLocation: v,
                },
              });
            }}
            dataSource={[
              { label: t('Prompt'), value: undefined },
              ...coverSaveLocations.map(c => ({
                label: t(c.label),
                value: c.value,
              })),
            ]}
          />
        );
      },
    },
    {
      label: 'Overwrite existed cover',
      tip: 'Overwrite existed cover when save a new cover',
      renderCell: () => {
        return (
          // @ts-ignore
          <Radio.Group
            value={resourceOptions?.coverOptions?.overwrite ?? undefined}
            onChange={(v) => {
              applyPatches(PatchResourceOptions, {
                coverOptions: {
                  ...(resourceOptions?.coverOptions ?? {}),
                  overwrite: v,
                },
              });
            }}
            dataSource={[
              { label: t('Prompt'), value: undefined },
              { label: t('Yes'), value: true },
              { label: t('No'), value: false },
            ]}
          />
        );
      },
    },
    {
      label: 'External search engines',
      tip: 'You can set external search engines for searching resource by name quickly in resource list, ' +
        "'{keyword}' will replaced by resource name or filename.",
      renderCell: () => {
        const addBtn = (
          <Button
            type={'normal'}
            size={'small'}
            onClick={() => {
              const newSimpleSearchEngines = simpleSearchEngines || [];
              newSimpleSearchEngines.push({
                key: uuidv4(),
              });
              setSimpleSearchEngines([...newSimpleSearchEngines]);
            }}
          >{t('Add')}
          </Button>
        );
        if (simpleSearchEngines?.length > 0) {
          return (
            <List
              size="small"
              header={
                <div style={{ display: 'flex', gap: '10px' }}>
                  {addBtn}
                  <Button
                    type={'primary'}
                    size={'small'}
                    onClick={() => {
                      if (simpleSearchEngines?.some((e) => !(e.name?.length > 0) || !(e.urlTemplate?.length > 0))) {
                        return Message.error(i18n.t('Name or url template can not be empty'));
                      }
                      applyPatches(PatchThirdPartyOptions, {
                        simpleSearchEngines: simpleSearchEngines || [],
                      });
                    }}
                  >
                    {t('Save')}
                  </Button>
                </div>
              }
              dataSource={simpleSearchEngines || []}
              renderItem={(item, i) => (
                <List.Item
                  key={i}
                  title={(
                    <div style={{ display: 'flex', gap: '10px', paddingBottom: '5px' }}>
                      <Input
                        defaultValue={item.name}
                        addonTextBefore={t('Name')}
                        size={'small'}
                        onChange={(v) => {
                          item.name = v;
                        }}
                      />
                      <div className="confirmation-button-group">
                        <ConfirmationButton
                          size={'small'}
                          label={'Delete'}
                          warning
                          icon={'delete'}
                          confirmation
                          onClick={(e) => {
                            simpleSearchEngines.splice(i, 1);
                            setSimpleSearchEngines([...simpleSearchEngines]);
                          }}
                        />
                      </div>
                    </div>
                  )}
                >
                  <Input
                    defaultValue={item.urlTemplate}
                    placeholder={i18n.t('{keyword} will be replaced by subject')}
                    addonTextBefore={t('Url template')}
                    size={'small'}
                    onChange={(v) => {
                      item.urlTemplate = v;
                    }}
                  />
                </List.Item>
              )}
            />
          );
        } else {
          return addBtn;
        }
      },
    },
    {
      label: 'ExHentai',
      tip: "Cookie is required for this feature. The format of excluded tags is something like 'language:chinese', " +
        "and you can use * to replace namespace or tag, 'language:*' for example.",
      renderCell: () => {
        return (
          <div className={'exhentai-options'}>
            <div>
              <Input
                size={'small'}
                addonTextBefore={'Cookie'}
                value={tmpExHentaiOptions.cookie}
                onChange={(v) => {
                  setTmpExHentaiOptions({
                    ...tmpExHentaiOptions,
                    cookie: v,
                  });
                }}
              />
            </div>
            <div>
              <Select
                style={{ width: '100%' }}
                size={'small'}
                label={(
                  <Balloon.Tooltip trigger={(t('Excluded tags'))}>
                    {t('You can filter some namespaces and tags such as \'language:*\' for ignoring all tags in language namespace')}
                  </Balloon.Tooltip>
                )}
                value={tmpExHentaiOptions?.enhancer?.excludedTags}
                dataSource={tmpExHentaiOptions?.enhancer?.excludedTags?.map((e) => ({ label: e, value: e }))}
                mode="tag"
                onChange={(v) => {
                  setTmpExHentaiOptions({
                    ...tmpExHentaiOptions,
                    enhancer: {
                      ...(tmpExHentaiOptions?.enhancer || {}),
                      excludedTags: v,
                    },
                  });
                }}
              />
            </div>
            <div className={'operations'}>
              <Button
                type={'primary'}
                size={'small'}
                onClick={() => {
                  applyPatches(PatchExHentaiOptions, tmpExHentaiOptions);
                }}
              >{t('Save')}
              </Button>
              <Button
                type={'normal'}
                size={'small'}
                disabled={!(tmpExHentaiOptions?.cookie?.length > 0) || validatingExHentaiCookie}
                loading={validatingExHentaiCookie}
                onClick={() => {
                  setValidatingExHentaiCookie(true);
                  ValidateCookie({
                    cookie: tmpExHentaiOptions.cookie,
                    target: CookieValidatorTarget.ExHentai,
                  }).invoke((r) => {
                    if (r.code) {
                      Message.error(`${t('Invalid cookie')}:${r.message}`);
                    } else {
                      Message.success(t('Cookie is good'));
                    }
                  }).finally(() => {
                    setValidatingExHentaiCookie(false);
                  });
                }}
              >{t('Validate cookie')}
              </Button>
            </div>
          </div>
        );
      },
    },
    {
      label: 'Startup page',
      renderCell: () => {
        console.log(uiOptions);
        return (
          <Radio.Group
            value={uiOptions.startupPage}
            onChange={(v) => {
              applyPatches(PatchUIOptions, {
                startupPage: v,
              });
            }}
          >
            {startupPages.map((s) => {
              return (
                <Radio key={s.value} value={s.value}>{t(s.label)}</Radio>
              );
            })}
          </Radio.Group>
        );
      },
    },
    {
      label: 'Exit behavior',
      renderCell: () => {
        // console.log(uiOptions);
        return (
          <Radio.Group
            value={appOptions.closeBehavior}
            onChange={(v) => {
              applyPatches(PatchAppOptions, {
                closeBehavior: v,
              });
            }}
          >
            {[CloseBehavior.Minimize, CloseBehavior.Exit, CloseBehavior.Prompt].map(c => (
              <Radio key={c} value={c}>{t(CloseBehavior[c])}</Radio>
            ))}
          </Radio.Group>
        );
      },
    },
  ];

  return (
    <div className="group">
      <Title title={i18n.t('Functional configurations')} />
      <div className="settings">
        <Table
          dataSource={functionSettings}
          size={'small'}
          hasHeader={false}
          cellProps={(r, c) => {
            return {
              className: c == 0 ? 'key' : c == 1 ? 'value' : '',
            };
          }}
        >
          <Table.Column
            width={300}
            dataIndex={'label'}
            cell={(c, i, r) => (
              <>
                {t(c)}
                {r.tip && (
                  <Balloon.Tooltip
                    key={i}
                    trigger={(
                      <CustomIcon type={'question-circle'} />
                    )}
                    triggerType={'hover'}
                    align={'t'}
                  >
                    {t(r.tip)}
                  </Balloon.Tooltip>
                )}
              </>
            )}
          />
          <Table.Column dataIndex={'renderCell'} cell={(r) => r()} />
        </Table>
      </div>
    </div>
  );
};
