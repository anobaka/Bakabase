import { FieldBinaryOutlined, RetweetOutlined, SisternodeOutlined, WarningOutlined } from '@ant-design/icons';
import React, { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import type { OnDeleteMatcherValue } from '../models';
import type PscMatcher from '../models/PscMatcher';
import type { IPscPropertyMatcherValue } from '../models/PscPropertyMatcherValue';
import { PscContext } from '../models/PscContext';
import PscProperty from '../models/PscProperty';
import { PscMatcherValue } from '../models/PscMatcherValue';
import type { SegmentMatcherConfigurationProps } from '../SegmentMatcherConfiguration';
import SegmentMatcherConfiguration from '../SegmentMatcherConfiguration';
import { ResourceMatcherValueType, ResourceProperty, ResourcePropertyType } from '@/sdk/constants';
import type { ChipProps } from '@/components/bakaui';
import { Chip, Listbox, ListboxItem, Popover, Tooltip } from '@/components/bakaui';
import BusinessConstants from '@/components/BusinessConstants';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import PropertySelector from '@/components/PropertySelector';
import { PscPropertyType } from '@/components/PathSegmentsConfiguration/models/PscPropertyType';
import SelectiveMatcher = PscContext.SelectiveMatcher;

type Props = {
  segments: PscContext.Segment[];
  isDirectory: boolean;
  value: IPscPropertyMatcherValue[];
  onChange: (value: IPscPropertyMatcherValue[]) => any;
  onDeleteMatcherValue: OnDeleteMatcherValue;
  visibleMatchers: PscMatcher[];
};

export default ({
                  segments,
                  isDirectory,
                  value,
                  onChange,
                  onDeleteMatcherValue,
                  visibleMatchers,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const rootDomRef = useRef<HTMLDivElement>(null);

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

  const buildSelectiveMatcherRightContent = (m: PscContext.SelectiveMatcher): React.ReactNode[] => {
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

  const selectMatcher = (c: PscMatcher, property: PscProperty, newValue: PscMatcherValue) => {
    let pvs = value.filter(v => v.property.equals(property));
    const nv = value.filter(v => !pvs.includes(v));
    if (!c.multiple) {
      pvs.splice(0, pvs.length);
    }

    const sameValue = pvs.find(v => v.property.equals(property) && v.value?.equals(newValue));
    if (!sameValue) {
      pvs.push({
        property,
        value: newValue,
      });
    }

    nv.push(...pvs);
    console.log('Changing value', nv, c, property, newValue, sameValue);
    onChange([...nv]);
  };

  const renderSegments = (): any => {
    if (segments.length > 0) {
      const elements: any[] = [];

      const rootPathSegmentIndex = segments.findIndex(s => s.matchResults.some(b => b.property.isRootPath));
      const resourceSegmentIndex = segments.findIndex(s => s.matchResults.some(b => b.property.isResource));

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
          <div className={`${disabled ? 'cursor-default opacity-60' : 'cursor-pointer'} px-2 py-1 hover:bg-[var(--bakaui-default)] rounded`} key={i}>
            {matchResults.length > 0 && (
              <div className={'flex items-start gap-1 flex-wrap'}>
                {matchResults.map(mr => {
                  const {
                    property,
                    errors = [],
                  } = mr;
                  const hasError = errors.length > 0;
                  const values = value.filter(v => v.property.equals(mr.property));
                  const v = values[mr.valueIndex ?? 0]?.value;
                  let colorKey: ChipProps['color'] = 'primary';
                  if (!mr.property.isCustom && (mr.property.id == ResourceProperty.Resource || mr.property.id == ResourceProperty.RootPath)) {
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
                      {property.toString(t, mr.valueIndex)}
                      &nbsp;
                      {v && (<span>{PscMatcherValue.ToString(t, v)}</span>)}
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
            {rootDomRef.current && (
              <Popover
                trigger={(
                  <div className="text-lg">
                    {text}
                  </div>
                )}
                portalContainer={rootDomRef.current}
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
                            selectMatcher(visibleMatchers.find(t => t.propertyType == m.propertyType)!, PscProperty.fromPscType(m.propertyType), new PscMatcherValue({
                              valueType: ResourceMatcherValueType.FixedText,
                              fixedText: segments.map(s => s.text).slice(0, i + 1).join(BusinessConstants.pathSeparator),
                            }));
                          } else {
                            if (m.useSmc) {
                              const props: Omit<SegmentMatcherConfigurationProps, 'property' | 'onSubmit'> = {
                                segments: segments.map(s => s.text),
                                segmentMarkers: {
                                  [rootPathSegmentIndex]: 'root',
                                  [resourceSegmentIndex]: 'resource',
                                  [i]: 'current',
                                },
                                modesData: m.buildModesData(),
                              };
                              switch (m.propertyType) {
                                case PscPropertyType.RootPath:
                                case PscPropertyType.ParentResource:
                                case PscPropertyType.Resource:
                                case PscPropertyType.Rating:
                                case PscPropertyType.Introduction:
                                {
                                  const property = PscProperty.fromPscType(m.propertyType);
                                  createPortal(SegmentMatcherConfiguration, {
                                    ...props,
                                    property,
                                    onSubmit: value => {
                                      selectMatcher(visibleMatchers.find(t => t.propertyType == m.propertyType)!, property, value);
                                    },
                                  });
                                  break;
                                }
                                case PscPropertyType.CustomProperty: {
                                  createPortal(PropertySelector, {
                                    pool: ResourcePropertyType.Custom,
                                    multiple: false,
                                    addable: true,
                                    onSubmit: async (selection) => {
                                      const p = selection[0];
                                      const property = new PscProperty({
                                        id: p.id,
                                        isCustom: true,
                                        name: p.name!,
                                      });
                                      createPortal(SegmentMatcherConfiguration, {
                                        ...props,
                                        property,
                                        onSubmit: value => {
                                          selectMatcher(visibleMatchers.find(t => t.propertyType == m.propertyType)!, property, value);
                                        },
                                      });
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
                              <span className={'text-base'}>{t(PscPropertyType[m.propertyType])}</span>
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
            )}
          </div>
        );

        elements.push(sc);
      }

      return (
        <div className={'flex flex-wrap items-center gap-1'}>
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
      ref={rootDomRef}
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
