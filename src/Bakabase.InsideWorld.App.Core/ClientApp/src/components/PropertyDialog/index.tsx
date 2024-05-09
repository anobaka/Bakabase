import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Dropdown, DropdownMenu, DropdownItem, DropdownTrigger, RadioGroup, Radio } from '@nextui-org/react';
import AceEditor from 'react-ace';
import ChoiceList from './components/ChoiceList';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyType, StandardValueConversionLoss } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import { Button, Chip, Input, Modal, Popover, Progress, Select, Switch, Tab, Tabs } from '@/components/bakaui';
import type {
  ChoicePropertyOptions,
  NumberPropertyOptions,
  PercentagePropertyOptions,
  IProperty,
  RatingPropertyOptions,
} from '@/components/Property/models';
import { PropertyTypeIconMap } from '@/components/Property/models';
import 'ace-builds/src-noconflict/mode-javascript';
import 'ace-builds/src-noconflict/theme-monokai';
import 'ace-builds/src-noconflict/ext-language_tools';

interface IProps {
  value?: CustomPropertyForm;
  onSaved?: (property: Omit<IProperty, 'isReserved'>) => any;
  validValueTypes?: CustomPropertyType[];
}

interface CustomPropertyForm {
  id?: number;
  name?: string;
  type?: CustomPropertyType;
  options?: any;
}

const PropertyTypeGroup: Record<string, CustomPropertyType[]> = {
  Text: [CustomPropertyType.SingleLineText, CustomPropertyType.MultilineText, CustomPropertyType.Link],
  Number: [CustomPropertyType.Number, CustomPropertyType.Percentage, CustomPropertyType.Rating],
  Option: [CustomPropertyType.SingleChoice, CustomPropertyType.MultipleChoice],
  DateTime: [CustomPropertyType.DateTime, CustomPropertyType.Date, CustomPropertyType.Time],
  Other: [
    CustomPropertyType.Attachment,
    CustomPropertyType.Boolean,
    // CustomPropertyType.Formula
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

const PropertyDialog = ({
                          value,
                          onSaved,
                          validValueTypes,
                          ...props
                        }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>(value || {});

  const [typeGroupsVisible, setTypeGroupsVisible] = useState(false);

  const close = () => {
    setVisible(false);
  };

  // console.log(6666, property);

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
                label={t('Allow adding new options while choosing')}
                size={'sm'}
                isSelected={options?.allowAddingNewDataDynamically}
                onValueChange={c => {
                  setProperty({
                    ...property,
                    options: {
                      ...options,
                      allowAddingNewOptionsWhileChoosing: c,
                    },
                  });
                }}
              />
              <Select
                className={'mt-2'}
                size={'sm'}
                label={t('Default value')}
                selectionMode={multiple ? 'multiple' : 'single'}
                selectedKeys={options?.defaultValue}
                dataSource={options?.choices?.map(choice => ({
                  label: choice.value,
                  value: choice.id,
                }))}
                onSelectionChange={c => {
                  const array = Array.from(c.values());
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
                      precision: c.values().next().value,
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
                      precision: c.values().next().value,
                    },
                  });
                }}
              />
              <Switch
                label={t('Show progressbar')}
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
              />
              {options?.showProgressbar ? (
                <div>
                  <div>{t('Preview')}</div>
                  <Progress
                    className={'max-w-[70%]'}
                    renderPercent={p => (
                      <div className={'text-[color:var(--bakaui-color)]'}>{previewValueStr}</div>
                    )}
                    percent={previewValue}
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
                      maxValue: c.values().next().value,
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
      }
    }
    return;
  };

  return (
    <Modal
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
            valueType: rsp.data!.valueType!,
            type: rsp.data!.type!,
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
            onVisibleChange={visible => setTypeGroupsVisible(visible)}
            trigger={(
              <Button
                color={'default'}
                size={'lg'}
                onClick={() => setTypeGroupsVisible(true)}
              >
                {property.type == undefined ? t('Select a type') : (
                  <>
                    <CustomIcon type={PropertyTypeIconMap[property.type]!} className={'text-medium'} />
                    {t(CustomPropertyType[property.type])}
                  </>
                )}
              </Button>
            )}
            placement={'right'}
          >
            <div className={'p-2 flex flex-col gap-2'}>
              {Object.keys(PropertyTypeGroup).map(group => {
                return (
                  <div className={'pb-2 mb-2 border-b-1 last:mb-0 last:border-b-0 last:pb-0'}>
                    <div className={'mb-2 font-bold'}>{t(group)}</div>
                    <div className="grid grid-cols-2 gap-x-2 text-sm leading-5">
                      {PropertyTypeGroup[group].map(type => {
                        return (
                          <Button
                            disabled={validValueTypes?.includes(type) === false}
                            variant={'light'}
                            className={'justify-start'}
                            onClick={() => {
                              if (property.id != undefined && property.id > 0) {
                                // change property type
                                const model = Modal.show({
                                  title: t('You are changing property type'),
                                  children: t('Changing the property type may cause the loss of existing data. Click \'continue\' to check.'),
                                  footer: {
                                    actions: ['ok', 'cancel'],
                                    okProps: {
                                      children: t('Continue'),
                                    },
                                  },
                                  onOk: async () => {
                                    const rsp = await BApi.customProperty.calculateCustomPropertyTypeConversionLoss(property.id!, type);
                                    if (rsp.data) {
                                      const {
                                        totalDataCount,
                                        incompatibleDataCount,
                                        lossData,
                                      } = rsp.data;
                                      const lossKeys = lossData ? Object.keys(lossData) : undefined;
                                      Modal.show({
                                        title: t('Final check'),
                                        size: incompatibleDataCount! > 0 ? 'lg' : undefined,
                                        children: (
                                          <div>
                                            <div className={'text-medium'}>
                                              {incompatibleDataCount! > 0 ? t('Found {{count}} data, and {{incompatibleDataCount}} data will not be retained', {
                                                count: totalDataCount!,
                                                incompatibleDataCount: incompatibleDataCount,
                                              }) : t('Found {{count}} data, and all of them will be retained', { count: totalDataCount! })}
                                            </div>
                                            <div className={'font-bold'}>
                                              {t('Be careful, this process is irreversible')}
                                            </div>
                                            {lossKeys && lossKeys.length > 0 && (
                                              <Tabs>
                                                {lossKeys.map(k => {
                                                  const loss = parseInt(k, 10) as StandardValueConversionLoss;
                                                  const data = lossData![k];
                                                  return (
                                                    <Tab
                                                      key={loss}
                                                      className={'mt-2'}
                                                      title={(
                                                        <>
                                                          {t(`StandardValueConversionLoss.${StandardValueConversionLoss[loss]}`)}
                                                          ({data.length})
                                                        </>
                                                      )}
                                                    >
                                                      <div className={'flex flex-wrap gap-2'}>
                                                        {data.map(d => {
                                                          return (
                                                            <Chip size={'sm'}>
                                                              {d}
                                                            </Chip>
                                                          );
                                                        })}
                                                      </div>
                                                    </Tab>
                                                  );
                                                })}
                                              </Tabs>
                                            )}
                                          </div>
                                        ),
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
                              } else {
                                setProperty({
                                  ...property,
                                  type,
                                });
                              }
                              setTypeGroupsVisible(false);
                            }}
                          >
                            <CustomIcon type={PropertyTypeIconMap[type]!} className={'text-medium'} />
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
