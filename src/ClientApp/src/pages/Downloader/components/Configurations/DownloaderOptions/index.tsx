import React, { useEffect, useState } from 'react';
import './index.scss';
import { Balloon, Input, NumberPicker } from '@alifd/next';
import i18n from 'i18next';
import { useUpdateEffect } from 'react-use';
import CookieValidator from '@/components/CookieValidator';
import { CookieValidatorTarget, ThirdPartyId } from '@/sdk/constants';
import FileSelector from '@/components/FileSelector';
import CustomIcon from '@/components/CustomIcon';

type VisibleKey = 'cookie' | 'threads' | 'interval' | 'defaultdownloadpath' | 'namingconvention';

// todo: extract abstractions
const getCookieValidatorTarget = (tpId: ThirdPartyId) => {
  switch (tpId) {
    case ThirdPartyId.Bilibili:
      return CookieValidatorTarget.BiliBili;
    case ThirdPartyId.Pixiv:
      return CookieValidatorTarget.Pixiv;
    case ThirdPartyId.ExHentai:
      return CookieValidatorTarget.ExHentai;
  }
};

export default ({
  GetApi, thirdPartyId, visibleKeys = [],
  namingDefinitions,
  onChange = (options) => {},
}: any & {visibleKeys: VisibleKey[]}) => {
  const [options, setOptions] = useState();

  useEffect(() => {
    if (GetApi) {
      GetApi().invoke((a) => {
        setOptions(a.data);
      });
    }
  }, []);

  useUpdateEffect(() => {
  }, [namingDefinitions]);

  useUpdateEffect(() => {
    onChange(options);
  }, [options]);

  const patchOptionsLocally = (patches = {}) => {
    const newOptions = {
      ...(options || {}),
      ...patches,
    };
    setOptions(newOptions);
  };

  const renderOptions = () => {
    const items = [];
    console.log(options);
    for (const k of visibleKeys) {
      switch (k.toLowerCase()) {
        case 'cookie':
        {
          items.push({
            label: 'Cookie',
            component: (
              <CookieValidator
                cookie={options?.cookie}
                onChange={(cookie) => {
                  patchOptionsLocally({ cookie });
                }}
                target={getCookieValidatorTarget(thirdPartyId)}
              />
            ),
          });
          break;
        }
        case 'defaultdownloadpath':
        {
          items.push({
            label: 'Default download path',
            component: (
              <FileSelector
                size={'small'}
                type={'folder'}
                value={options?.downloader?.defaultPath}
                multiple={false}
                onChange={(defaultPath) => patchOptionsLocally({
                  downloader: {
                    ...(options?.downloader || {}),
                    defaultPath,
                  },
                })}
              />
            ),
          });
          break;
        }
        case 'threads':
        {
          items.push({
            label: 'Threads',
            tip: i18n.t('If you are browsing {{thirdPartyName}}, you should decrease the threads of downloading.', { lowerCasedThirdPartyName: ThirdPartyId[thirdPartyId].toLowerCase() }),
            component: (
              <NumberPicker
                min={0}
                max={5}
                step={1}
                value={options?.downloader?.threads}
                onChange={(threads) => patchOptionsLocally({
                  downloader: {
                    ...(options?.downloader || {}),
                    threads,
                  },
                })}
              />
            ),
          });
          break;
        }
        case 'interval':
        {
          items.push({
            label: 'Request interval',
            component: (
              <NumberPicker
                style={{ width: 250 }}
                min={0}
                max={9999999}
                innerAfter={i18n.t('ms')}
                value={options?.downloader?.interval}
                onChange={(interval) => patchOptionsLocally({
                  downloader: {
                    ...(options?.downloader || {}),
                    interval,
                  },
                })}
              />
            ),
          });
          break;
        }
        case 'namingconvention':
        {
          const { fields: namingFields = [], defaultConvention } = namingDefinitions || {};

          const currentConvention = options?.downloader?.namingConvention ?? defaultConvention;
          let namingPathSegments = [];
          if (currentConvention) {
            namingPathSegments = namingFields.reduce((s, t) => {
              if (t.example) {
                return s.replace(new RegExp(`\\{${t.key}\\}`, 'g'), t.example);
              }
              return s;
            }, currentConvention).replace(/\\/g, '/').split('/');
          }
          items.push({
            label: 'Naming convention',
            className: 'naming-convention',
            tip: i18n.t('You can select fields to build a naming convention template, and \'/\' to create directory.'),
            component: (
              <>
                <Input.TextArea
                  autoHeight
                  placeholder={defaultConvention}
                  style={{ width: '100%' }}
                  value={options?.downloader?.namingConvention}
                  onChange={(v) => {
                    patchOptionsLocally({
                      downloader: {
                        ...(options?.downloader || {}),
                        namingConvention: v,
                      },
                    });
                  }}
                />
                {currentConvention && (
                <div className="example">
                  {i18n.t('Example')}:&nbsp;
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
                          patchOptionsLocally({
                            downloader: {
                              ...(options?.downloader || {}),
                              namingConvention: options?.downloader?.namingConvention?.length > 0 ? `${options?.downloader?.namingConvention}${value}` : value,
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
                          {i18n.t(f.description)}
                        </Balloon.Tooltip>
                      );
                    } else {
                      return tag;
                    }
                  })}
                </div>
              </>
            ),
          });
          break;
        }
      }
    }

    if (items.length > 0) {
      return (
        <div className={'form'}>
          <div className="items">
            {items.map((t) => {
              let { tip } = t;
              if (typeof tip === 'string' || tip instanceof String) {
                tip = i18n.t(tip);
              }
              return (
                <>
                  <div className="label">
                    {i18n.t(t.label)}
                    {tip && (
                      <Balloon
                        closable={false}
                        trigger={(
                          <CustomIcon type={'question-circle'} size={'small'} />
                            )}
                        triggerType={'hover'}
                        align={'r'}
                      >
                        {tip}
                      </Balloon>
                    )}
                  </div>
                  <div className={`value ${t.className ? t.className : ''}`}>{t.component}</div>
                </>
              );
            })}
          </div>
        </div>
      );
    } else {
      return i18n.t('For now, there is nothing to set');
    }
  };

  // console.log(namingDefinitions, options);

  return (
    <div className={'downloader-options'}>
      {/* <div className="title"> */}
      {/*   <div className="placeholder" /> */}
      {/*   {i18n.t(ThirdPartyId[thirdPartyId])} */}
      {/* </div> */}
      {renderOptions()}
    </div>
  );
};
