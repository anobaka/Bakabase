import { FieldBinaryOutlined, RetweetOutlined, SisternodeOutlined, WarningOutlined } from '@ant-design/icons';
import React from 'react';
import { useTranslation } from 'react-i18next';
import type { OnDeleteMatcherValue } from '../models';
import type { PscCoreData } from '@/components/PathSegmentsConfiguration/models/PscCoreData';
import { ResourceMatcherValueType, ResourceProperty } from '@/sdk/constants';
import type { ChipProps } from '@/components/bakaui';
import { Chip, Listbox, ListboxItem, Modal, Popover, Tooltip } from '@/components/bakaui';
import { type IMatcherValue, MatcherValue } from '@/components/PathSegmentsConfiguration/models/MatcherValue';
import BusinessConstants from '@/components/BusinessConstants';
import SegmentMatcherConfiguration from '@/components/PathSegmentsConfiguration/SegmentMatcherConfiguration';
import FileSystemEntryIcon from '@/components/FileSystemEntryIcon';
import type PathSegmentMatcher from '@/components/PathSegmentsConfiguration/models/PathSegmentMatcher';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Props = {
  segments: PscCoreData.Segment[];
  isDirectory: boolean;
  value: { [type in ResourceProperty]?: MatcherValue[]; };
  onChange: (value: { [type in ResourceProperty]?: MatcherValue[]; }) => any;
  onDeleteMatcherValue: OnDeleteMatcherValue;
};

export default ({ segments, isDirectory, value, onChange, onDeleteMatcherValue }: Props) => {
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

  const selectMatcher = (c: PathSegmentMatcher, newValue: IMatcherValue) => {
    const currentValues = value[c.property] || [];
    if (!c.multiple) {
      currentValues.splice(0, currentValues.length);
    }

    const sameValue = currentValues.find(v => v.equals(newValue));
    if (sameValue) {
      return;
    }
    currentValues.push(new MatcherValue(newValue));

    const newAllValue = {
      ...value,
      [c.property]: currentValues,
    };
    console.log('Changing value', newAllValue);
    onChange(newAllValue);
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
                  const v = mr.valueIndex == undefined ? value[mr.property]?.[0] : value[mr.property]?.[mr.valueIndex];
                  let colorKey: ChipProps['color'] = 'primary';
                  if (mr.property == ResourceProperty.Resource || mr.property == ResourceProperty.RootPath) {
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
                <div className={'mb-4'}>
                  <div className={'font-bold text-xl'}>{t('Setting a Resource property to segment')}</div>
                  <div
                    className={'italic opacity-60 mt-1'}
                  >{t('Some properties may not able to set by layer, but you still can set it by regex.')}</div>
                </div>
                <div>
                  <Listbox
                    variant={'bordered'}
                    onAction={k => {
                      const m = selectiveMatchers[k];
                      const o = m.matchModes.oneClick;
                      if (o.available) {
                        selectMatcher(visibleMatchers.find(t => t.property == m.property)!, {
                          type: ResourceMatcherValueType.FixedText,
                          fixedText: segments.slice(0, i + 1).join(BusinessConstants.pathSeparator),
                        });
                      } else {
                        if (m.useSmc) {
                          switch (m.property) {
                            case ResourceProperty.RootPath:
                            case ResourceProperty.ParentResource:
                            case ResourceProperty.Resource: {
                              createPortal(Modal, {
                                defaultVisible: true,
                                size: 'lg',
                                children: (
                                  <SegmentMatcherConfiguration
                                    property={m.property}
                                    modesData={m.buildModesData()}
                                    isCustomProperty={m.property == ResourceProperty.CustomProperty}
                                    onSubmit={value => {
                                      selectMatcher(visibleMatchers.find(t => t.property == m.property)!, value);
                                    }}
                                  />
                                ),
                              });
                              break;
                            }
                            case ResourceProperty.CustomProperty:
                              break;
                          }
                        }
                      }
                    }}
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
