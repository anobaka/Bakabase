import IceLabel from '@icedesign/label';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ResourceProperty } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';
import { Button, Chip, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { convertToPathConfigurationDtoFromPscValue } from '@/components/PathSegmentsConfiguration/helpers';
import type { IPscPropertyMatcherValue } from '@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue';

type ResultEntry = {
  isDirectory: boolean;
  relativePath: string;
  segmentAndMatchedValues: {
    value: string;
    properties: {
      property: ResourceProperty;
      keys: string[];
    }[];
  }[];
  globalMatchedValues: {
    property: ResourceProperty;
    key?: string;
    values: string[];
  }[];
};

type TestResult = {
  entries: ResultEntry[];
  rootPath: string;
};

type Props = {
  isDisabled: boolean;
  value: IPscPropertyMatcherValue[];
};


export default ({ isDisabled, value }: Props) => {
  const { t } = useTranslation();

  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<TestResult>();

  return (
    <>
      <Button
        disabled={isDisabled}
        color={'primary'}
        size={'small'}
        isLoading={loading}
        onClick={() => {
          setLoading(true);
          BApi.mediaLibrary.validatePathConfiguration(convertToPathConfigurationDtoFromPscValue(value))
            .then((t) => {
              // @ts-ignore
              setResult(t.data);
            })
            .finally(() => {
              setLoading(false);
            });
        }}
      >
        {t('Validate')}
      </Button>
      <Modal
        onClose={() => {
          setResult(undefined);
        }}
        visible={!!result}
        title={t('Found top {{count}} resources. (shows up to 100 results)', { count: (result?.entries || []).length })}
        footer={{
          actions: ['cancel'],
          cancelProps: {
            children: t('Close'),
          },
        }}
      >
        <section className="mt-1">
          {result && result.entries && result.entries.length > 0 && (
            <div className="mt-1 flex flex-col gap-1">
              {
                result.entries.map((e, i) => {
                  const {
                    globalMatchedValues,
                    isDirectory,
                    segmentAndMatchedValues,
                  } = e;

                  console.log(globalMatchedValues);

                  const segments: any[] = [];
                  for (let j = 0; j < segmentAndMatchedValues?.length; j++) {
                    const ps = segmentAndMatchedValues[j];
                    const matchLabels: string[] = [];
                    if (j == segmentAndMatchedValues.length - 1) {
                      matchLabels.push(t(ResourceProperty[ResourceProperty.Resource]));
                    }
                    if (ps.properties?.length > 0) {
                      for (const p of ps.properties) {
                        if (p.property == ResourceProperty.CustomProperty) {
                          matchLabels.push(`${t('Custom property')}:${p.keys.join(',')}`);
                        } else {
                          matchLabels.push(t(ResourceProperty[p.property]));
                        }
                      }
                    }
                    // console.log(types, ps);
                    segments.push(
                      <div className={'flex flex-col'}>
                        {matchLabels.length > 0 && (
                          <div
                            className={`text-sm ${matchLabels.length > 1 ? 'conflict' : ''}`}
                            style={{ color: matchLabels.length > 1 ? 'var(--bakaui-danger)' : 'var(--bakaui-primary)' }}
                          >
                            {matchLabels.join(', ')}
                          </div>
                        )}
                        <div className="text-sm">{ps.value}</div>
                      </div>,
                    );
                    if (j != e.segmentAndMatchedValues.length - 1) {
                      segments.push(
                        <span
                          className={''}
                          style={{ color: 'var(--bakaui-warning)' }}
                        >/</span>,
                      );
                    }
                  }

                  const globalMatchesElements: any[] = [];
                  if (globalMatchedValues.length > 0) {
                    for (const gmv of globalMatchedValues) {
                      const {
                        property,
                        key,
                        values,
                      } = gmv;
                      const propertyLabel = property == ResourceProperty.CustomProperty ? `${t('Custom property')}:${key}` : t(ResourceProperty[property]);
                      if (values?.length > 0) {
                        globalMatchesElements.push(
                          <div className={'flex items-center gap-1'}>
                            <div className="text-sm font-bold">{propertyLabel}</div>
                            <div className="flex flex-wrap gap-1">
                              {values.map(v => (
                                <Chip size={'sm'} radius={'sm'}>{v}</Chip>
                              ))}
                            </div>
                          </div>,
                        );
                      }
                    }
                  }

                  return (
                    <div className={'flex items-center p-1 rounded border-default-200'}>
                      <div className="mr-2">
                        <Chip
                          size={'sm'}
                          radius={'sm'}
                        >{i + 1}</Chip>
                      </div>
                      <div className="grow flex flex-col gap-1">
                        <div className="flex items-center gap-1">
                          <CustomIcon type={isDirectory ? 'folder' : 'file'} className={'text-sm'} />
                          {segments}
                        </div>
                        {globalMatchesElements.length > 0 && (
                          <div className="flex items-start flex-wrap gap-1 border-default-200 pt-1">
                            {globalMatchesElements}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })
              }
            </div>
          )}
        </section>
      </Modal>
    </>
  );
};
