import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { Balloon, Input, NumberPicker } from '@alifd/next';
import i18n from 'i18next';
import { useUpdateEffect } from 'react-use';
import { useTranslation } from 'react-i18next';
import { Textarea } from '@heroui/react';
import CookieValidator from '@/components/CookieValidator';
import type { CookieValidatorTarget } from '@/sdk/constants';
import { ThirdPartyId } from '@/sdk/constants';
import FileSelector from '@/components/FileSelector';
import CustomIcon from '@/components/CustomIcon';
import { NumberInput } from '@/components/bakaui';
import type { components } from '@/sdk/BApi2';
import store from '@/store';
import type { OptionsStore } from '@/models/options';

type ConfigurableKey = 'cookie' | 'threads' | 'interval' | 'defaultDownloadPath' | 'namingConvention';

type NamingDefinition = components['schemas']['Bakabase.InsideWorld.Models.Models.Aos.DownloaderNamingDefinitions'];

type Options = {
  cookie?: string;
  downloader?: {
    defaultPath?: string;
    threads?: number;
    interval?: number;
    namingConvention?: string;
  };
};

type Props = {
  thirdPartyId: ThirdPartyId;
  options: Options;
  namingDefinition?: NamingDefinition;
  configurableKeys: ConfigurableKey[];
  onChange: (patches: Partial<Options>) => void;
};

export default ({
  thirdPartyId,
  options,
  configurableKeys = [],
  namingDefinition,
  onChange,
}: Props) => {
  const { t } = useTranslation();

  const renderOptions = () => {
    const items: any[] = [];
    for (const k of configurableKeys) {
      switch (k) {
        case 'cookie':
        {
          items.push(
            <>
              <div>{t('Cookie')}</div>
              <div>
                <CookieValidator
                  cookie={options?.cookie}
                  onChange={(cookie) => {
                    onChange({ cookie });
                  }}
                  target={thirdPartyId as unknown as CookieValidatorTarget}
                />
              </div>
            </>,
          );
          break;
        }
        case 'defaultDownloadPath':
        {
          items.push(
            <>
              <div>{t('Default download path')}</div>
              <div>
                <FileSelector
                  size={'small'}
                  type={'folder'}
                  value={options?.downloader?.defaultPath}
                  multiple={false}
                  onChange={(defaultPath) => onChange({
                    downloader: {
                      ...(options?.downloader || {}),
                      defaultPath: defaultPath as string,
                    },
                  })}
                />
              </div>
            </>,
          );
          break;
        }
        case 'threads':
        {
          items.push(
            <>
              <div>{t('Threads')}</div>
              <div>
                <NumberInput
                  min={0}
                  max={5}
                  step={1}
                  value={options?.downloader?.threads}
                  onChange={(threads) => onChange({
                  downloader: {
                    ...(options?.downloader || {}),
                    threads,
                  },
                })}
                  description={t('If you are browsing {{thirdPartyName}}, you should decrease the threads of downloading.', { lowerCasedThirdPartyName: ThirdPartyId[thirdPartyId].toLowerCase() })}
                />
              </div>
            </>,
        );
          break;
        }
        case 'interval':
        {
          items.push(
            <>
              <div>{t('Request interval')}</div>
              <div>
                <NumberInput
                  style={{ width: 250 }}
                  min={0}
                  max={9999999}
                  endContent={t('ms')}
                  value={options?.downloader?.interval}
                  onChange={(interval) => onChange({
                    downloader: {
                      ...(options?.downloader || {}),
                      interval,
                    },
                  })}
                />
              </div>
            </>,
          );
          break;
        }
        case 'namingConvention':
        {
          const { fields: namingFields = [], defaultConvention } = namingDefinition || {};
          const currentConvention = options?.downloader?.namingConvention ?? defaultConvention;
          let namingPathSegments: string[] = [];
          if (currentConvention) {
            namingPathSegments = namingFields.reduce((s, t) => {
              if (t.example) {
                return s.replace(new RegExp(`\\{${t.key}\\}`, 'g'), t.example);
              }
              return s;
            }, currentConvention).replace(/\\/g, '/').split('/');
          }
          items.push(
            <>
              <div>{t('Naming convention')}</div>
              <div>
                <Textarea
                  placeholder={defaultConvention}
                  style={{ width: '100%' }}
                  value={options?.downloader?.namingConvention}
                  onChange={(v) => {
                    onChange({
                      downloader: {
                        ...(options?.downloader || {}),
                        namingConvention: v,
                      },
                    });
                  }}
                  description={t('You can select fields to build a naming convention template, and \'/\' to create directory.')}
                />
                {currentConvention && (
                  <div className="example">
                    {t('Example')}:&nbsp;
                    <div>
                      {namingPathSegments.map((t, i) => {
                        if (i == namingPathSegments.length - 1) {
                          return (
                            <span className={'segment'}>{t}</span>
                          );
                        } else {
                          return (
                            <>
                              <span className={'segment'}>{t}</span>
                              <span className={'separator'}>/</span>
                            </>
                          );
                        }
                      })}
                    </div>
                  </div>
                )}
                <div className={'fields'}>
                  {namingFields.map((f) => {
                    const tag = (
                      <div
                        className={'field'}
                        onClick={() => {
                          const value = `{${f.key}}`;
                          let nc = value;
                          if (options?.downloader?.namingConvention && options.downloader.namingConvention.length > 0) {
                            nc = `${options?.downloader?.namingConvention}${value}`;
                          }
                          onChange({
                            downloader: {
                              ...(options?.downloader || {}),
                              namingConvention: nc,
                            },
                          });
                        }}
                      >
                        <div className="key">
                          {f.key}
                        </div>
                        {f.example?.length > 0 && (
                          <div className={'example'}>{f.example}</div>
                        )}
                      </div>
                    );
                    if (f.description) {
                      return (
                        <Balloon.Tooltip
                          align={'t'}
                          triggerType={'hover'}
                          trigger={tag}
                        >
                          {t(f.description)}
                        </Balloon.Tooltip>
                      );
                    } else {
                      return tag;
                    }
                  })}
                </div>
              </div>
            </>,
          );
          break;
        }
      }
    }
    return items;
  };

  // console.log(namingDefinitions, options);

  return (
    <div className={'grid gap-x-2 gap-y-1'} style={{ gridTemplateColumns: 'auto 1fr' }}>
      {renderOptions()}
    </div>
  );
};
