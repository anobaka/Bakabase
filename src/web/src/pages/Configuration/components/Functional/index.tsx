'use client';

import i18n from 'i18next';
import { toast } from '@/components/bakaui';
import {Balloon, Checkbox, Input, List, Radio, Select} from '@alifd/next';
import React, { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import CustomIcon from '@/components/CustomIcon';
import {
  AdditionalCoverDiscoveringSource,
  additionalCoverDiscoveringSources,
  CloseBehavior,
  CookieValidatorTarget,
  DependentComponentStatus,
  startupPages,
} from '@/sdk/constants';
import { useAppOptionsStore, useResourceOptionsStore, useExHentaiOptionsStore, useThirdPartyOptionsStore, useUiOptionsStore } from '@/models/options';
import { useDependentComponentContextsStore } from '@/models/dependentComponentContexts';
import type { BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions } from '@/sdk/Api';
import { findCapturingGroupsInRegex, uuidv4 } from '@/components/utils';
import dependentComponentIds from '@/core/models/Constants/DependentComponentIds';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import {
  Button,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Textarea,
  Tooltip,
} from '@/components/bakaui';
import BApi from '@/sdk/BApi';

export default ({ applyPatches = () => {} }: {applyPatches: (API: any, patches: any) => void}) => {
  const { t } = useTranslation();

  const appOptions = useAppOptionsStore((state) => state.data);
  const resourceOptions = useResourceOptionsStore((state) => state.data);
  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);
  const thirdPartyOptions = useThirdPartyOptionsStore((state) => state.data);
  const [validatingExHentaiCookie, setValidatingExHentaiCookie] = useState(false);
  const uiOptions = useUiOptionsStore((state) => state.data);

  const [simpleSearchEngines, setSimpleSearchEngines] = useState<BakabaseInsideWorldModelsConfigsThirdPartyOptionsSimpleSearchEngineOptions[]>([]);
  const [tmpExHentaiOptions, setTmpExHentaiOptions] = useState(exhentaiOptions || {});

  const ffmpegState = useDependentComponentContextsStore((state) => state.contexts)?.find(d => d.id == dependentComponentIds.FFMpeg);

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
                  <Balloon.Tooltip trigger={(t<string>('Excluded tags'))}>
                    {t<string>('You can filter some namespaces and tags such as \'language:*\' for ignoring all tags in language namespace')}
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
                color={'primary'}
                size={'sm'}
                onClick={() => {
                  applyPatches(BApi.options.patchExHentaiOptions, tmpExHentaiOptions);
                }}
              >{t<string>('Save')}
              </Button>
              <Button
                size={'sm'}
                disabled={!(tmpExHentaiOptions?.cookie?.length > 0) || validatingExHentaiCookie}
                isLoading={validatingExHentaiCookie}
                onClick={() => {
                  setValidatingExHentaiCookie(true);
                  BApi.tool.validateCookie({
                    cookie: tmpExHentaiOptions.cookie,
                    target: CookieValidatorTarget.ExHentai,
                  }).then((r) => {
                    if (r.code) {
                      toast.error(`${t<string>('Invalid cookie')}:${r.message}`);
                    } else {
                      toast.success(t<string>('Cookie is good'));
                    }
                  }).finally(() => {
                    setValidatingExHentaiCookie(false);
                  });
                }}
              >{t<string>('Validate cookie')}
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
              applyPatches(BApi.options.patchUIOptions, {
                startupPage: v,
              });
            }}
          >
            {startupPages.map((s) => {
              return (
                <Radio key={s.value} value={s.value}>{t<string>(s.label)}</Radio>
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
              applyPatches(BApi.options.patchAppOptions, {
                closeBehavior: v,
              });
            }}
          >
            {[CloseBehavior.Minimize, CloseBehavior.Exit, CloseBehavior.Prompt].map(c => (
              <Radio key={c} value={c}>{t<string>(CloseBehavior[c])}</Radio>
            ))}
          </Radio.Group>
        );
      },
    },
  ];

  return (
    <div className="group">
      {/* <Title title={i18n.t<string>('Functional configurations')} /> */}
      <div className="settings">
        <Table
          removeWrapper
        >
          <TableHeader>
            <TableColumn width={200}>{t<string>('Functional configurations')}</TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            {functionSettings.map((c, i) => {
              return (
                <TableRow key={i} className={'hover:bg-[var(--bakaui-overlap-background)]'}>
                  <TableCell>
                    <div style={{ display: 'flex', alignItems: 'center' }}>
                      {t<string>(c.label)}
                      {c.tip && (
                        <>
                          &nbsp;
                          <Tooltip
                            placement={'right'}
                            content={t<string>(c.tip)}
                          >
                            <CustomIcon type={'question-circle'} className={'text-base'} />
                          </Tooltip>
                        </>
                      )}
                    </div>
                  </TableCell>
                  <TableCell>
                    {c.renderCell()}
                  </TableCell>
                </TableRow>
              );
            })}

          </TableBody>
        </Table>
      </div>
    </div>
  );
};
