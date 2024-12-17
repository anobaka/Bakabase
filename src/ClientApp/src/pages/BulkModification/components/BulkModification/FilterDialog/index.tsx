import React, { useCallback, useRef, useState } from 'react';
import { Balloon, Button, DatePicker2, Dialog, Input, NumberPicker, Radio, Select, Tag } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { filterTargetComponents, FilterTargetComponentValueType, FindInAliasesProperties } from '../FilterDialog/models';
import type { IBulkModificationFilter } from '../../BulkModification';
import ValueRenderer from './components/ValueRenderer';
import {
  BulkModificationFilterOperation,
  bulkModificationFilterOperations,
  bulkModificationProperties,
  BulkModificationProperty,
  resourceLanguages,
} from '@/sdk/constants';
import './index.scss';
import { useUpdate, useUpdateEffect } from 'react-use';
import BApi from '@/sdk/BApi';
import { createPortalOfComponent } from '@/components/utils';
import CustomIcon from '@/components/CustomIcon';
import ResourceCategorySelector from '@/components/ResourceCategorySelector';
import { Tag as TagDto } from '@/core/models/Tag';

interface IProps {
  filter?: IBulkModificationFilter;
  onSubmit?: (value: IBulkModificationFilter) => any;
}

const isInvalid = (filter?: IBulkModificationFilter) => {
  if (!filter) {
    return true;
  }

  if (filter.property == BulkModificationProperty.CustomProperty && (filter.propertyKey == undefined || filter.propertyKey.length == 0)) {
    return true;
  }

  if (!filter.operation) {
    return true;
  }

  if (filter.operation != BulkModificationFilterOperation.IsNull && filter.operation != BulkModificationFilterOperation.IsNotNull) {
    if (filter.target == undefined || filter.target.length == 0) {
      return true;
    }
  }

  return false;
};

const FilterDialog = (props: IProps) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();

  const {
    filter: propsFilter,
    onSubmit,
  } = props;
  const [visible, setVisible] = useState(true);
  const [filter, setFilter] = useState<IBulkModificationFilter>(propsFilter || {});
  const propertyKeyCandidatesRef = useRef<{ label: string; value: string }[]>();
  const tagsCandidatesRef = useRef<{ label: string; value: string }[]>();

  useUpdateEffect(() => {
    if (filter.property == BulkModificationProperty.CustomProperty && !propertyKeyCandidatesRef.current) {
      propertyKeyCandidatesRef.current = [];
      // get all custom property keys

      BApi.resource.getAllCustomPropertyKeys().then(r => {
        const keys = r.data || [];
        propertyKeyCandidatesRef.current = keys.map(k => ({
          label: k,
          value: k,
        }));
        forceUpdate();
      });
    }

    if (filter.property == BulkModificationProperty.Tag && !tagsCandidatesRef.current) {
      tagsCandidatesRef.current = [];
      // get all tags
      BApi.tag.getAllTags().then(r => {
        const tags = r.data || [];
        tagsCandidatesRef.current = tags.map(t => {
          // @ts-ignore
          const dto = new TagDto(t);
          return {
            label: dto.displayName,
            value: dto.preferredAlias ?? dto.name,
          };
        });
        forceUpdate();
      });
    }
  }, [filter?.property]);

  const renderCustomPropertyKeySelector = () => {
    if (filter.property != BulkModificationProperty.CustomProperty) {
      return null;
    }
    return (
      <>
        <div className="label">
          {t('Property Key')}
        </div>
        <div className="value">
          <Select
            dataSource={propertyKeyCandidatesRef.current || []}
            value={filter?.propertyKey}
            onChange={v => setFilter({
              ...filter,
              propertyKey: v,
            })}
            useVirtual
            showSearch
          />
        </div>
      </>
    );
  };

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  const renderDynamicPart = () => {
    if (filter) {
      if (filter.operation && filter.property) {
        const elements: any[] = [];
        if (FindInAliasesProperties.includes(filter.property!)) {
          elements.push((
            <div className={'label'} key={'find-in-aliases-label'}>
              {t('Find in aliases')}
              <Balloon.Tooltip
                trigger={(
                  <CustomIcon type={'question-circle'} />
                )}
                triggerType={'hover'}
                align={'t'}
                v2
              >
                {t('Some properties have applied aliases, you can enable this feature to check your target both in raw values and aliases.')}
              </Balloon.Tooltip>
            </div>
          ));
          elements.push((
            <div className={'value'} key={'find-in-aliases-value'}>
              <Radio.Group
                dataSource={[{
                  label: t('Enable'),
                  value: 1,
                },
                  // , {
                  //   label: t('Disable'),
                  //   value: 0,
                  // }
                ]}
                defaultValue={1}
              />
            </div>
          ));
        }

        const component = filterTargetComponents[filter.property]?.[filter.operation];

        // console.log('Rendering component', component);

        if (component) {
          let targetComponent: any;

          let typedTarget: any;
          if (filter.target != undefined) {
            typedTarget = JSON.parse(filter.target);
            if (component.multiple && !Array.isArray(typedTarget)) {
              typedTarget = [];
            }
          }

          let useValueRenderer = true;

          switch (component.valueType) {
            case FilterTargetComponentValueType.None:
              break;
            case FilterTargetComponentValueType.Text: {
              if (component.multiple) {
                let compValue: any;
                targetComponent = (
                  <Input
                    key={'multiple'}
                    placeholder={t('Press enter to add a item to list')}
                    onChange={v => compValue = v}
                    onKeyDown={e => {
                      if (e.key == 'Enter') {
                        if (compValue) {
                          (typedTarget ??= []).push(compValue);
                          setFilter({
                            ...filter,
                            target: JSON.stringify(typedTarget),
                          });
                        }
                      }
                    }}
                  />
                );
              } else {
                targetComponent = (
                  <Input
                    key={'single'}
                    value={typedTarget}
                    onChange={v => {
                      setFilter({
                        ...filter,
                        target: JSON.stringify(v),
                      });
                    }}
                  />
                );
                useValueRenderer = false;
              }
              break;
            }
            case FilterTargetComponentValueType.Number: {
              if (component.multiple) {
                let compValue: any;
                targetComponent = (
                  <NumberPicker
                    precision={2}
                    key={'multiple'}
                    placeholder={t('Press enter to add a item to list')}
                    onChange={v => compValue = v}
                    onKeyDown={e => {
                      if (e.key == 'Enter') {
                        if (compValue) {
                          (typedTarget ??= []).push(compValue);
                          setFilter({
                            ...filter,
                            target: JSON.stringify(typedTarget),
                          });
                        }
                      }
                    }}
                  />
                );
              } else {
                targetComponent = (
                  <NumberPicker
                    precision={2}
                    key={'single'}
                    value={typedTarget}
                    onChange={v => {
                      setFilter({
                        ...filter,
                        target: JSON.stringify(v),
                      });
                    }}
                  />
                );
                useValueRenderer = false;
              }
              break;
            }
            case FilterTargetComponentValueType.Language: {
              if (component.multiple) {
                targetComponent = (
                  <Select
                    dataSource={resourceLanguages.map(r => ({ ...r, label: t(r.label) }))}
                    value={undefined}
                    key={'multiple'}
                    onChange={v => {
                      if (v) {
                        (typedTarget ??= []).push(parseInt(v, 10));
                        setFilter({
                          ...filter,
                          target: JSON.stringify(typedTarget),
                        });
                      }
                    }}
                  />
                );
              } else {
                targetComponent = (
                  <Select
                    dataSource={resourceLanguages.map(r => ({ ...r, label: t(r.label) }))}
                    key={'single'}
                    value={typedTarget}
                    onChange={v => {
                      setFilter({
                        ...filter,
                        target: JSON.stringify(parseInt(v, 10)),
                      });
                    }}
                  />
                );
                useValueRenderer = false;
              }
              break;
            }
            case FilterTargetComponentValueType.Datetime: {
              if (component.multiple) {
                targetComponent = (
                  <DatePicker2
                    showTime
                    timePanelProps={{
                      defaultValue: '00:00:00',
                    }}
                    value={undefined}
                    key={'multiple'}
                    onChange={v => {
                      if (v) {
                        (typedTarget ??= []).push(v.format('YYYY-MM-DD HH:mm:ss'));
                        setFilter({
                          ...filter,
                          target: JSON.stringify(typedTarget),
                        });
                      }
                    }}
                  />
                );
              } else {
                targetComponent = (
                  <DatePicker2
                    timePanelProps={{
                      defaultValue: '00:00:00',
                    }}
                    showTime
                    key={'single'}
                    value={typedTarget}
                    onChange={v => {
                      setFilter({
                        ...filter,
                        target: JSON.stringify(v.format('YYYY-MM-DD HH:mm:ss')),
                      });
                    }}
                  />
                );
                useValueRenderer = false;
              }
              break;
            }
            case FilterTargetComponentValueType.Category: {
              targetComponent = (
                <ResourceCategorySelector
                  value={undefined}
                  onChange={v => {
                    const newTarget = component.multiple ? (typedTarget || []).concat(v) : v;
                    setFilter({
                      ...filter,
                      target: JSON.stringify(newTarget),
                    });
                  }}
                />
              );
              break;
            }
            case FilterTargetComponentValueType.MediaLibrary: {
              const compValue = component.multiple ? typedTarget : typedTarget && [typedTarget];
              targetComponent = (
                <Button
                  size={'small'}
                  type={'normal'}
                  onClick={() => {
                    let value = compValue;
                    Dialog.show({
                      title: t('Select media libraries'),
                      v2: true,
                      width: 'auto',
                      content: (
                        <MediaLibrarySelectPanel
                          defaultValue={compValue}
                          multiple={component.multiple}
                          onChange={v => {
                            value = v;
                          }}
                        />
                      ),
                      closeMode: ['close', 'esc', 'mask'],
                      onOk: () => {
                        setFilter({
                          ...filter,
                          target: JSON.stringify(component.multiple ? value : value[0]),
                        });
                      },
                    });
                  }}
                >{t('Select media libraries')}</Button>
              );
              break;
            }
            case FilterTargetComponentValueType.Tag: {
              const compValue = component.multiple ? typedTarget : typedTarget && [typedTarget];
              targetComponent = (
                // <Button
                //   size={'small'}
                //   type={'normal'}
                //   onClick={() => {
                //     let value = compValue;
                //     Dialog.show({
                //       title: t('Select tags'),
                //       v2: true,
                //       width: 'auto',
                //       content: (
                //         <TagSelector
                //           multiple={component.multiple}
                //           defaultValue={{ tagIds: compValue || [] }}
                //           onChange={v => {
                //             value = v.tagIds;
                //           }}
                //         />
                //       ),
                //       closeMode: ['close', 'esc', 'mask'],
                //       onOk: () => {
                //         setFilter({
                //           ...filter,
                //           target: JSON.stringify(component.multiple ? value : value[0]),
                //         });
                //       },
                //     });
                //   }}
                // >{t('Select tags')}</Button>
                <Select
                  showSearch
                  dataSource={tagsCandidatesRef.current}
                  mode={component.multiple ? 'tag' : 'single'}
                  onChange={v => {
                    setFilter({
                      ...filter,
                      target: JSON.stringify(v),
                    });
                  }}
                />
              );
              break;
            }
          }

          if (targetComponent) {
            elements.push(
              <div className="label" key={'target-label'}>
                {t('Target')}
              </div>,
            );

            elements.push(
              <div className="value" key={'target-value'}>
                {useValueRenderer && (
                  <ValueRenderer
                    removable={component.multiple}
                    key={filter.property}
                    target={filter.target}
                    onChange={v => setFilter({
                      ...filter,
                      target: v,
                    })}
                    property={filter.property}
                  />
                )}
                {targetComponent}
              </div>,
            );
          }
        }

        return elements;
      }
    }
    return null;
  };

  const availableOperations: BulkModificationFilterOperation[] = filter.property
    ? Object.keys(filterTargetComponents[filter.property] || []).map(o => parseInt(o, 10))
    : [];

  // console.log(filter);

  return (
    <Dialog
      visible={visible}
      v2
      width={'auto'}
      className={'bulk-modification-filter-dialog'}
      closeMode={['close', 'esc']}
      onClose={close}
      onCancel={close}
      okProps={{
        disabled: isInvalid(filter),
      }}
      onOk={() => {
        close();
        onSubmit && onSubmit(filter);
      }}
    >
      <div className="filter-form">
        <div className="label">
          {t('Property')}
        </div>
        <div className="value">
          <Tag.Group>
            {bulkModificationProperties.map(p => {
              const currChecked = filter.property == p.value;

              return (
                <Tag.Selectable
                  key={p.value}
                  onChange={checked => {
                    if (!currChecked && checked) {
                      setFilter({
                        ...filter,
                        property: p.value,
                        operation: undefined,
                        target: undefined,
                      });
                    }
                  }}
                  checked={currChecked}
                >{t(p.label)}</Tag.Selectable>
              );
            })}
          </Tag.Group>
        </div>
        {renderCustomPropertyKeySelector()}
        <div className="label">
          {t('Operation')}
        </div>
        <div className="value">
          <Tag.Group>
            {bulkModificationFilterOperations.map(op => {
              const currChecked = filter.operation == op.value;
              return (
                <Tag.Selectable
                  key={op.value}
                  onChange={checked => {
                    if (!currChecked && checked) {
                      setFilter({
                        ...filter,
                        operation: op.value,
                        target: undefined,
                      });
                    }
                  }}
                  checked={currChecked}
                  disabled={!availableOperations.includes(op.value)}
                >{t(`BulkModificationFilterOperation.${BulkModificationFilterOperation[op.value]}`)}</Tag.Selectable>
              );
            })}
          </Tag.Group>
        </div>
        {renderDynamicPart()}
      </div>
    </Dialog>
  );
};

FilterDialog.show = (props: IProps) => createPortalOfComponent(FilterDialog, props);

export default FilterDialog;
