import { FieldBinaryOutlined, RetweetOutlined, SisternodeOutlined, WarningOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import type { OnDeleteMatcherValue } from '../models';
import type { IMatcherValue, PropertyMatcherValues } from '../models/MatcherValue';
import { MatcherValue } from '../models/MatcherValue';
import { PscCoreData } from '@/components/PathSegmentsConfiguration/models/PscCoreData';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import type { ChipProps } from '@/components/bakaui';
import { Chip, Listbox, ListboxItem, Modal, Popover, Tooltip } from '@/components/bakaui';
import BusinessConstants from '@/components/BusinessConstants';
import type { SegmentMatcherConfigurationProps } from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import SegmentMatcherConfiguration from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import type PscMatcher from '@/components/PathSegmentsConfiguration/models/PscMatcher';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import SelectiveMatcher = PscCoreData.SelectiveMatcher;
import PropertySelector from '@/components/PropertySelector';
import type PscProperty from '@/components/PathSegmentsConfiguration/models/PscProperty';

type Props = {
  segments: PscCoreData.Segment[];
  isDirectory: boolean;
  value: PropertyMatcherValues[];
  onChange: (value: PropertyMatcherValues[]) => any;
  onDeleteMatcherValue: OnDeleteMatcherValue;
  visibleMatchers: PscMatcher[];
};

export default ({ segments, isDirectory, value, onChange, onDeleteMatcherValue, visibleMatchers }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const buildMatchModeTip = (name: string, icon: React.ReactNode, available: boolean, errors: string[]) => {
    const inner = (
      <Chip
        variant={'faded'}
        startContent={icon}
        size={'sm'}
        radius={'sm'}
        color={available ? 'success' : 'warning'}
      >{
        t(available ? '{{mode}} mode is available' : '{{mode}} mode is unavailable', { mode: name })
      }
      </Chip>
    );

    if (available || errors.length == 0) {
      return inner;
    }
    return (
      <Tooltip
        content={(
          <div>
            {errors.length > 0 && (
              <ul>
                {errors.map(e => (
                  <li>{e}</li>
                ))}
              </ul>
            )}
          </div>
        )}
      >
        {inner}
      </Tooltip>
    );
  };

  const buildSelectiveMatcherRightContent = (m: PscCoreData.SelectiveMatcher): React.ReactNode[] => {
    const {
      layer: l,
      regex: r,
      oneClick: o,
    } = m.matchModes;

    const showReplace = m.isConfigurable && m.replaceCurrent;

    const firstLineNodes: React.ReactNode[] = [
      buildMatchModeTip(t('Layer'), <SisternodeOutlined className={'text-sm'} />, l.available, l.errors),
      buildMatchModeTip(t('Regex'), <FieldBinaryOutlined className={'text-sm'} />, r.available, r.errors),
    ];

    if (showReplace) {
      firstLineNodes.push(
        <Chip
          variant={'faded'}
          startContent={<RetweetOutlined className={'text-sm'} />}
          size={'sm'}
          radius={'sm'}
          color={'warning'}
        >
          {t('Selected value will be replaced')}
        </Chip>,
      );
    }

    return firstLineNodes;
  };

  const selectMatcher = (c: PscMatcher, property: PscProperty, newValue: IMatcherValue) => {
    let pvs = value.find(v => v.property.equals(property));
    if (!pvs) {
      pvs = {
        property,
        values: [],
      };
      value.push(pvs);
    }
    if (!c.multiple) {
      pvs.values.splice(0, pvs.values.length);
    }

    const sameValue = pvs.values.find(v => v.equals(newValue));
    if (!sameValue) {
      pvs.values.push(new MatcherValue(newValue));
    }

    console.log('Changing value', value);
    onChange([...value]);
  };

  const renderSegments = (): any => {
    if (segments.length > 0) {
      const elements: any[] = [];

      for (let i = 0; i < segments.length; i++) {
        if (i > 0) {
          elements.push(
            <span className={'text-xl'} style={{ color: 'var(--bakaui-warning)' }}>/</span>,
          );
        }

        const {
          text,
          selectiveMatchers = [],
          matchResults = [],
          disabled,
        } = segments[i];

        const sc = (
          <div className={`segment-container ${disabled ? 'disabled' : ''} rounded`} key={i}>
            {matchResults.length > 0 && (
              <div className={'flex items-start gap-1 flex-wrap'}>
                {matchResults.map(mr => {
                  const {
                    label,
                    errors = [],
                  } = mr;
                  const hasError = errors.length > 0;
                  const values = value.find(v => v.property.equals(mr.property))?.values || [];
                  const v = mr.valueIndex == undefined ? values[0] : values[mr.valueIndex];
                  let colorKey: ChipProps['color'] = 'primary';
                  if (mr.property.isReserved && (mr.property.id == ResourceProperty.Resource || mr.property.id == ResourceProperty.RootPath)) {
                    colorKey = 'success';
                  }
                  if (hasError) {
                    colorKey = 'danger';
                  }
                  const comp = (
                    <Chip
                      size={'sm'}
                      color={colorKey}
                      radius={'sm'}
                      variant={'bordered'}
                      // className={'matched-matcher border-1 rounded'}
                      // style={colorStyle}
                      onClose={!mr.readonly ? () => {
                        onDeleteMatcherValue(mr.property, mr.valueIndex);
                      } : undefined}
                    >
                      {hasError && (<WarningOutlined className={'text-sm'} />)}
                      {label}
                      &nbsp;
                      {v && (<span>{MatcherValue.ToString(v)}</span>)}
                    </Chip>
                  );
                  if (hasError) {
                    return (
                      <Tooltip
                        content={errors.map(e => {
                          return (
                            <div
                              className={'error'}
                              style={{
                                display: 'flex',
                                alignItems: 'center',
                                gap: 2,
                                color: 'red',
                              }}
                            >
                              <WarningOutlined className={'text-sm'} />
                              {e}
                            </div>
                          );
                        })}
                        placement={'top'}
                      >
                        {comp}
                      </Tooltip>
                    );
                  } else {
                    return comp;
                  }
                })}
              </div>
            )}
            <Popover
              trigger={(
                <div className="text-lg">
                  {text}
                </div>
              )}
            >
              <div className="p-4">
                <div className={'mb-2'}>
                  <div className={'font-bold text-xl'}>{t('Mark this path segment as')}</div>
                </div>
                <div>
                  <Listbox
                    variant={'bordered'}
                    onAction={k => {
                      console.log('on action', k);
                      const m: SelectiveMatcher = selectiveMatchers[k];
                      if (m.isConfigurable) {
                        const o = m.matchModes.oneClick;
                        if (o.available) {
                          selectMatcher(visibleMatchers.find(t => t.propertyType == m.propertyType)!, {
                            id: m.property,
                          }, {
                            type: ResourceMatcherValueType.FixedText,
                            fixedText: segments.map(s => s.text).slice(0, i + 1).join(BusinessConstants.pathSeparator),
                          });
                        } else {
                          if (m.useSmc) {
                            const props: SegmentMatcherConfigurationProps = {
                              property: {
                                isReserved: true,
                                id: m.property,
                                name: t(ResourceProperty[m.property]),
                              },
                              segments: segments.map(s => s.text),
                              segmentIndex: i,
                              modesData: m.buildModesData(),
                              onSubmit: value => {
                                selectMatcher(visibleMatchers.find(t => t.propertyType == m.property)!, value);
                              },
                            };
                            switch (m.property) {
                              case ResourceProperty.RootPath:
                              case ResourceProperty.ParentResource:
                              case ResourceProperty.Resource: {
                                createPortal(SegmentMatcherConfiguration, props);
                                break;
                              }
                              case ResourceProperty.CustomProperty: {
                                createPortal(PropertySelector, {
                                  pool: 'custom',
                                  multiple: false,
                                  addable: true,
                                  onSubmit: async (selection) => {
                                    const p = selection[0];
                                    props.property = {
                                      id: p.id,
                                      isReserved: false,
                                      name: p.name!,
                                    };
                                    createPortal(SegmentMatcherConfiguration, props);
                                  },
                                });
                                break;
                              }
                            }
                          }
                        }
                      }
                    }}
                    disabledKeys={selectiveMatchers.filter(m => !m.isConfigurable).map(m => selectiveMatchers.indexOf(m)).map(x => x.toString())}
                  >
                    {selectiveMatchers.map((m, i) => {
                      const rightContents = buildSelectiveMatcherRightContent(m);
                      return (
                        <ListboxItem
                          key={i}
                          description={m.errors.length > 0 && (
                            <ul style={{ color: 'var(--bakaui-danger)' }} className={'mt-1 flex flex-wrap gap-1'}>
                              {m.errors.map(e => (
                                <li>{e}</li>
                              ))}
                            </ul>)}
                        >
                          <div className={'flex items-center gap-4'}>
                            <span className={'text-base'}>{t(ResourceProperty[m.property])}</span>
                            <div className={'flex items-center gap-2'}>{rightContents}</div>
                          </div>
                        </ListboxItem>
                      );
                    })}
                  </Listbox>
                </div>
                <div
                  className={'italic opacity-60 mt-1'}
                >{t('Some properties may not able to set by layer, but you still can set it by regex.')}</div>
              </div>
            </Popover>
          </div>
        );

        elements.push(sc);
      }

      return (
        <div className={'path-segments'}>
          {elements}
        </div>
      );
    }
  };

  if (segments.length == 0) {
    return null;
  }

  return (
    <div
      className="path-segments-container flex items-center gap-2 border-small px-2 py-1 rounded-small border-default-200"
    >
      <div className="w-[24px] h-[24px] flex items-center justify-center">
        <FileSystemEntryIcon
          size={24}
          path={segments.map(s => s.text).join('/')}
          isDirectory={isDirectory}
        />
      </div>
      {renderSegments()}
    </div>
  );
};
