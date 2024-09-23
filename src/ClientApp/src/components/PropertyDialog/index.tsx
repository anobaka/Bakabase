import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Radio, RadioGroup, TableHeader } from '@nextui-org/react';
import AceEditor from 'react-ace';
import type { Key } from '@react-types/shared';
import ChoiceList from './components/ChoiceList';
import TagList from './components/TagList';
import { buildLogger, createPortalOfComponent } from '@/components/utils';
import { CustomPropertyType, ResourcePropertyType } from '@/sdk/constants';
import './index.scss';
import BApi from '@/sdk/BApi';
import {
  Button,
  Chip,
  Icon,
  Input,
  Modal,
  Popover,
  Progress,
  Select,
  Switch,
  Tab,
  Tabs,
  Tooltip,
  Table,
  TableColumn,
  TableBody,
  TableRow,
  TableCell,
} from '@/components/bakaui';
import type {
  ChoicePropertyOptions,
  IProperty,
  NumberPropertyOptions,
  PercentagePropertyOptions,
  RatingPropertyOptions,
  TagsPropertyOptions,
} from '@/components/Property/models';
import { PropertyTypeIconMap } from '@/components/Property/models';
import 'ace-builds/src-noconflict/mode-javascript';
import 'ace-builds/src-noconflict/theme-monokai';
import 'ace-builds/src-noconflict/ext-language_tools';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import FeatureStatusTip from '@/components/FeatureStatusTip';
import { optimizeOptions } from '@/components/PropertyDialog/helpers';
import PropertyValueRenderer from '@/components/Property/components/PropertyValueRenderer';
import ValueRenderer from '@/components/StandardValue/ValueRenderer';
import { deserializeStandardValue } from '@/components/StandardValue/helpers';
import type { DestroyableProps } from '@/components/bakaui/types';

interface IProps extends DestroyableProps{
  value?: CustomPropertyForm;
  onSaved?: (property: IProperty) => any;
  validValueTypes?: CustomPropertyType[];
}

interface CustomPropertyForm {
  id?: number;
  name?: string;
  type?: CustomPropertyType;
  options?: any;
}

const UnderDevelopmentGroupKey = 'UnderDevelopment';

const PropertyTypeGroup: Record<string, CustomPropertyType[]> = {
  Text: [CustomPropertyType.SingleLineText, CustomPropertyType.MultilineText, CustomPropertyType.Link],
  Number: [CustomPropertyType.Number, CustomPropertyType.Percentage, CustomPropertyType.Rating],
  Option: [CustomPropertyType.SingleChoice, CustomPropertyType.MultipleChoice],
  DateTime: [CustomPropertyType.DateTime, CustomPropertyType.Date, CustomPropertyType.Time],
  Other: [
    CustomPropertyType.Attachment,
    CustomPropertyType.Boolean,
    CustomPropertyType.Tags,
  ],
  [UnderDevelopmentGroupKey]: [
    CustomPropertyType.Formula,
    CustomPropertyType.Multilevel,
  ],
};

const NumberPrecisions = [0, 1, 2, 3, 4].map(x => ({
  value: x,
  label: Number(1).toFixed(x),
}));

const RatingMaxValueDataSource = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map(x => ({
  value: x,
  label: x,
}));

const log = buildLogger('PropertyDialog');

const PropertyDialog = ({
                          value,
                          onSaved,
                          validValueTypes,
                          ...props
                        }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>(JSON.parse(JSON.stringify({
    ...(value || {}),
    options: optimizeOptions(value?.options),
  })));

  const [typeGroupsVisible, setTypeGroupsVisible] = useState(false);

  const typePopoverDomRef = useRef<HTMLDivElement>(null);

  const close = () => {
    setVisible(false);
  };

  log(property);

  const checkValueUsage = property.id ? async (value: string) => {
    const rsp = await BApi.customProperty.getCustomPropertyValueUsage(property.id!, { value: value });
    return rsp.data!;
  } : undefined;

  const renderOptions = () => {
    if (property.type != undefined) {
      switch (property.type) {
        case CustomPropertyType.SingleLineText:
        case CustomPropertyType.MultilineText:
          break;
        case CustomPropertyType.SingleChoice:
        case CustomPropertyType.MultipleChoice: {
          const options = property.options as ChoicePropertyOptions;
          const multiple = property.type === CustomPropertyType.MultipleChoice;
          return (
            <>
              <ChoiceList
                className={'mt-4'}
                choices={options?.choices}
                checkUsage={checkValueUsage}
                onChange={choices => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      choices,
                    },
                  });
                }}
              />
              <Switch
                className={'mt-4'}
                size={'sm'}
                isSelected={options?.allowAddingNewDataDynamically}
                onValueChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      allowAddingNewDataDynamically: c,
                    },
                  });
                }}
              >
                {t('Allow adding new options while choosing')}
              </Switch>
              <Select
                className={'mt-2'}
                size={'sm'}
                label={t('Default value')}
                selectionMode={multiple ? 'multiple' : 'single'}
                selectedKeys={options?.defaultValue}
                dataSource={options?.choices}
                onSelectionChange={c => {
                  const array = Array.from((c as Set<Key>).values());
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      defaultValue: multiple ? array : array[0],
                    },
                  });
                }}
              />
            </>
          );
        }
        case CustomPropertyType.Number: {
          const options = property.options as NumberPropertyOptions ?? {};
          const previewValue = 80;
          const previewValueStr = Number(previewValue).toFixed(options?.precision || 0);
          // console.log(previewValue, options?.precision, previewValueStr);
          options.precision ??= 0;
          return (
            <>
              <Select
                label={t('Precision')}
                selectedKeys={[options.precision.toString()]}
                dataSource={NumberPrecisions}
                onSelectionChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      precision: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
              <Input
                label={t('Preview')}
                disabled
                value={previewValueStr}
              />
            </>
          );
        }
        case CustomPropertyType.Percentage: {
          const options = property.options as PercentagePropertyOptions ?? {};
          const previewValue = 80;
          const previewValueStr = `${Number(previewValue).toFixed(options?.precision || 0)}%`;
          options.precision ??= 0;
          return (
            <>
              <Select
                label={t('Precision')}
                selectedKeys={[options.precision.toString()]}
                dataSource={NumberPrecisions}
                onSelectionChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      precision: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
              <Switch
                size={'sm'}
                isSelected={options?.showProgressbar}
                onValueChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      showProgressbar: c,
                    },
                  });
                }}
              >{t('Show progressbar')}</Switch>
              {options?.showProgressbar ? (
                <div>
                  <div>{t('Preview')}</div>
                  <Progress
                    className={'max-w-[70%]'}
                    label={<div className={'text-[color:var(--bakaui-color)]'}>{previewValueStr}</div>}
                    value={previewValue}
                  />
                </div>
              ) : (
                <Input
                  label={t('Preview')}
                  disabled
                  value={previewValueStr}
                />
              )}
            </>
          );
        }
        case CustomPropertyType.Rating: {
          const options = property.options as RatingPropertyOptions ?? {};
          options.maxValue ??= 5;
          return (
            <>
              <Select
                label={t('Max value')}
                selectedKeys={[options.maxValue.toString()]}
                dataSource={RatingMaxValueDataSource}
                onSelectionChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      maxValue: (c as Set<Key>).values().next().value,
                    },
                  });
                }}
              />
            </>
          );
        }
        case CustomPropertyType.Boolean: {
          break;
        }
        case CustomPropertyType.Link:
        case CustomPropertyType.Attachment:
          break;
        case CustomPropertyType.Date:
          break;
        case CustomPropertyType.DateTime:
          break;
        case CustomPropertyType.Time:
          break;
        case CustomPropertyType.Formula: {
          return (
            <>
              <RadioGroup
                label={t('Formula syntax')}
                orientation="horizontal"
              >
                <Radio value="buenos-aires">Javascript</Radio>
              </RadioGroup>
              <AceEditor
                mode="javascript"
                theme="monokai"
                wrapEnabled
                onChange={v => {
                  console.log(v);
                }}
                name="UNIQUE_ID_OF_DIV"
                editorProps={{ $blockScrolling: true }}
              />,
            </>
          );
        }
        case CustomPropertyType.Multilevel: {
          break;
        }
        case CustomPropertyType.Tags:
        {
          const options = property.options as TagsPropertyOptions;
          return (
            <>
              <TagList
                className={'mt-4'}
                tags={options?.tags}
                onChange={tags => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      tags,
                    },
                  });
                }}
                checkUsage={checkValueUsage}
              />
              <Switch
                className={'mt-4'}
                size={'sm'}
                isSelected={options?.allowAddingNewDataDynamically}
                onValueChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      allowAddingNewDataDynamically: c,
                    },
                  });
                }}
              >
                {t('Allow adding new options while choosing')}
              </Switch>
            </>
          );
        }
      }
    }
    return;
  };

  const renderPropertyTypeButton = () => {
    const btn = (
      <Button
        color={'default'}
        size={'lg'}
        onClick={() => {
          // console.log(13456577, true);
          setTypeGroupsVisible(true);
        }}
      >
        {property.type == undefined ? t('Select a type') : (
          <>
            <Icon
              type={PropertyTypeIconMap[property.type]!}
              className={'text-base'}
            />
            {t(CustomPropertyType[property.type])}
          </>
        )}
      </Button>
    );
    if (property && property.id != undefined && property.id > 0) {
      // return btn;
      return (
        <div>
          <Tooltip content={t('Click to change type')}>
            <div>
              {btn}
            </div>
          </Tooltip>
        </div>
      );
    } else {
      return (
        <div>
          {btn}
        </div>
      );
    }
  };

  console.log(typeGroupsVisible);

  return (
    <Modal
      size={'lg'}
      visible={visible}
      title={t('Custom property')}
      onClose={close}
      onOk={async () => {
        const model = {
          ...property,
          // todo: this serialization is a hardcode
          options: JSON.stringify(property.options),
        };
        const rsp = (property.id != undefined && property.id > 0) ? await BApi.customProperty.putCustomProperty(property.id, model) : await BApi.customProperty.addCustomProperty(model);
        if (!rsp.code) {
          // console.log('on saved', onSaved);
          onSaved?.({
            id: rsp.data!.id!,
            name: rsp.data!.name!,
            dbValueType: rsp.data!.dbValueType!,
            bizValueType: rsp.data!.bizValueType!,
            customPropertyType: rsp.data!.type!,
            type: ResourcePropertyType.Custom,
          });
          close();
        }
      }}
      {...props}
    >
      <div className={'flex flex-col gap-2'}>
        <div className={'flex gap-2 items-center'}>
          <Popover
            closeMode={['mask', 'esc']}
            showArrow
            visible={typeGroupsVisible}
            onVisibleChange={visible => {
              // console.log(visible, 'event');
              setTypeGroupsVisible(visible);
            }}
            trigger={renderPropertyTypeButton()}
            placement={'right'}
          >
            <div className={'p-2 flex flex-col gap-2'} ref={typePopoverDomRef}>
              {Object.keys(PropertyTypeGroup).map(group => {
                return (
                  <div className={'pb-2 mb-2 border-b-1 last:mb-0 last:border-b-0 last:pb-0'}>
                    <div className={'mb-2 font-bold'}>{t(group)}</div>
                    <div className="grid grid-cols-3 gap-x-2 text-sm leading-5">
                      {PropertyTypeGroup[group].map(type => {
                        if (group == UnderDevelopmentGroupKey) {
                          return (
                            <Tooltip content={(
                              <FeatureStatusTip status={'developing'} name={t(CustomPropertyType[type])} />
                            )}
                            >
                              <Button
                                disabled={validValueTypes?.includes(type) === false}
                                variant={'light'}
                                className={'justify-start'}
                              >
                                <Icon type={PropertyTypeIconMap[type]!} className={'text-medium'} />
                                {t(CustomPropertyType[type])}
                              </Button>
                            </Tooltip>
                          );
                        }
                        return (
                          <Button
                            disabled={validValueTypes?.includes(type) === false}
                            variant={'light'}
                            className={'justify-start'}
                            onClick={() => {
                              if (property.id != undefined && property.id > 0) {
                                BApi.customProperty.getCustomPropertyConversionRules().then(r => {
                                  const rules = r.data?.[property.type!]?.[type] ?? [];
                                  // change property type
                                  const model = Modal.show({
                                    title: t('You are changing property type'),
                                    children: (
                                      <div>
                                        <div>
                                          {t('Changing the property type may cause the loss of existing data. Click \'continue\' to check.')}
                                        </div>
                                        {rules.length > 0 && (
                                          <div className={'mt-2'}>
                                            <div className={'font-bold'}>{t('Following rule(s) will be applied')}</div>
                                            <div className={'flex flex-wrap gap-2 items-center mt-1'}>
                                              {rules.map(r => {
                                                if (r.description == null) {
                                                  return (
                                                    <Chip size={'sm'}>{r.name}</Chip>
                                                  );
                                                }
                                                return (
                                                  <Tooltip content={(<pre>{r.description}</pre>)}>
                                                    <Chip size={'sm'}>{r.name}</Chip>
                                                  </Tooltip>
                                                );
                                              })}
                                            </div>
                                          </div>
                                        )}
                                      </div>
                                    ),
                                    footer: {
                                      actions: ['ok', 'cancel'],
                                      okProps: {
                                        children: t('Continue'),
                                      },
                                    },
                                    onOk: async () => {
                                      const rsp = await BApi.customProperty.previewCustomPropertyTypeConversion(property.id!, type);
                                      if (rsp.data) {
                                        const changes = rsp.data?.changes || [];
                                        const {
                                          dataCount,
                                          toType,
                                          fromType,
                                        } = rsp.data;
                                        Modal.show({
                                          title: t('Final check'),
                                          size: changes.length! > 0 ? 'lg' : undefined,
                                          children: (
                                            <div>
                                              <div className={'text-medium'}>
                                                {changes.length > 0 ? t('Found {{count}} data, and {{changedDataCount}} data will be modified or deleted', {
                                                  count: dataCount,
                                                  changedDataCount: changes.length!,
                                                }) : t('Found {{count}} data, and all of them will be retained', { count: dataCount! })}
                                              </div>
                                              <div className={'font-bold'}>
                                                {t('Be careful, this process is irreversible')}
                                              </div>
                                              {changes.length > 0 && (
                                                <Table>
                                                  <TableHeader>
                                                    <TableColumn>{t('Source value')}</TableColumn>
                                                    <TableColumn>{t('Converted value')}</TableColumn>
                                                  </TableHeader>
                                                  <TableBody>
                                                    {changes.map(c => {
                                                      return (
                                                        <TableRow>
                                                          <TableCell>
                                                            <ValueRenderer
                                                              type={fromType!}
                                                              value={deserializeStandardValue(c.serializedFromValue ?? null, fromType!)}
                                                              variant={'light'}
                                                            />
                                                          </TableCell>
                                                          <TableCell>
                                                            <ValueRenderer
                                                              type={toType!}
                                                              value={deserializeStandardValue(c.serializedToValue ?? null, toType!)}
                                                              variant={'light'}
                                                            />
                                                          </TableCell>
                                                        </TableRow>
                                                      );
                                                    })}
                                                  </TableBody>
                                                </Table>
                                              )}
                                            </div>
                                          ),
                                          onOk: async () => {
                                            await BApi.customProperty.changeCustomPropertyType(property.id!, type);
                                            await BApi.customProperty.getCustomPropertyByKeys({ ids: [property.id!] }).then(r => {
                                              // @ts-ignore
                                              setProperty(r.data[0]);
                                            });
                                          },
                                          footer: {
                                            actions: ['ok', 'cancel'],
                                            okProps: {
                                              children: t('Convert'),
                                            },
                                          },
                                        });
                                      }
                                    },
                                  });
                                });
                              } else {
                                setProperty({
                                  ...property,
                                  type,
                                });
                              }
                              setTypeGroupsVisible(false);
                            }}
                          >
                            <Icon type={PropertyTypeIconMap[type]!} className={'text-medium'} />
                            {t(CustomPropertyType[type])}
                          </Button>
                        );
                      })}
                    </div>
                  </div>
                );
              })}
            </div>
          </Popover>
          <Input
            className={'flex-1'}
            size={'sm'}
            label={t('Name')}
            value={property.name}
            onValueChange={name => setProperty({
              ...property,
              name,
            })}
          />
        </div>
        {renderOptions()}
      </div>
    </Modal>
  );
};


PropertyDialog.show = (props: IProps) => createPortalOfComponent(PropertyDialog, props);

export default PropertyDialog;
