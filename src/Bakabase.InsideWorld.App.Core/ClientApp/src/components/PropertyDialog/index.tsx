import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import ChoiceList from './components/ChoiceList';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyType } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';
import { Button, Input, Modal, Popover, Progress, Select, Switch } from '@/components/bakaui';
import type {
  IChoicePropertyOptions,
  INumberPropertyOptions,
  IPercentagePropertyOptions,
  IProperty,
  IRatingPropertyOptions,
} from '@/components/Property/models';
import { PropertyTypeIconMap } from '@/components/Property/models';

interface IProps {
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

const PropertyTypeGroup: Record<string, CustomPropertyType[]> = {
  Text: [CustomPropertyType.SingleLineText, CustomPropertyType.MultilineText, CustomPropertyType.Link],
  Number: [CustomPropertyType.Number, CustomPropertyType.Percentage, CustomPropertyType.Rating],
  Option: [CustomPropertyType.SingleChoice, CustomPropertyType.MultipleChoice],
  DateTime: [CustomPropertyType.DateTime, CustomPropertyType.Date, CustomPropertyType.Time],
  Other: [CustomPropertyType.Attachment, CustomPropertyType.Boolean],
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
          const options = property.options as IChoicePropertyOptions;
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
                isSelected={options?.allowAddingNewOptionsWhileChoosing}
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
                value={options?.defaultValue}
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
          const options = property.options as INumberPropertyOptions;
          const previewValue = 80;
          const previewValueStr = Number(previewValue).toFixed(options?.precision || 0);
          console.log(previewValue, options?.precision, previewValueStr);
          return (
            <>
              <Select
                label={t('Precision')}
                value={options?.precision}
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
          const options = property.options as IPercentagePropertyOptions;
          const previewValue = 80;
          const previewValueStr = `${Number(previewValue).toFixed(options?.precision || 0)}%`;
          return (
            <>
              <Select
                label={t('Precision')}
                value={options?.precision}
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
          const options = property.options as IRatingPropertyOptions;
          return (
            <>
              <div className="label">{t('Max value')}</div>
              <div className="value">
                <Select
                  value={options?.maxValue}
                  dataSource={RatingMaxValueDataSource}
                  onSelectionChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        maxValue: c,
                      },
                    });
                  }}
                />
              </div>
            </>
          );
          break;
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
        case CustomPropertyType.Formula:
          break;
        case CustomPropertyType.Multilevel:
          break;
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
          onSaved?.({
            id: rsp.data!.id!,
            name: rsp.data!.name!,
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
                {property.type == undefined ? t('Please select') : (
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
                              setTypeGroupsVisible(false);
                              setProperty({
                                ...property,
                                type,
                              });
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
