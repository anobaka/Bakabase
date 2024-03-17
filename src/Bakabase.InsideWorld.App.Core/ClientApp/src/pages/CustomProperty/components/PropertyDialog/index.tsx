import { Button, Dialog, Input, Overlay, Progress, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  PropertyTypeIconMap,
} from '../../models';
import type { IChoice, ICustomProperty,
  IChoicePropertyOptions,
  INumberPropertyOptions,
  IPercentagePropertyOptions, IRatingPropertyOptions } from '../../models';
import ChoiceList from './components/ChoiceList';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyType } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import BApi from '@/sdk/BApi';

const { Popup } = Overlay;

interface IProps extends DialogProps {
  value?: CustomPropertyForm;
  onSaved?: (property: ICustomProperty) => any;
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
                          ...dialogProps
                        }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>(value || {});

  const [typeGroupsVisible, setTypeGroupsVisible] = useState(false);

  const close = () => {
    setVisible(false);
  };

  // console.log(6666, 'render');

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
              <div className={'label'}>{t('Options')}</div>
              <div className="value">
                <ChoiceList
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
              </div>
              <div className="label">{t('Allow adding new options while choosing')}</div>
              <div className="value">
                <Switch
                  size={'small'}
                  checked={options?.allowAddingNewOptionsWhileChoosing}
                  onChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        allowAddingNewOptionsWhileChoosing: c,
                      },
                    });
                  }}
                />
              </div>
              <div className="label">{t('Default value')}</div>
              <div className="value">
                <Select
                  mode={multiple ? 'multiple' : 'single'}
                  value={options?.defaultValue}
                  autoWidth
                  hasClear
                  style={{ width: '100%' }}
                  dataSource={options?.choices?.map(choice => choice.value)}
                  showSearch
                  onChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        defaultValue: c,
                      },
                    });
                  }}
                />
              </div>
            </>
          );
        }
        case CustomPropertyType.Number: {
          const options = property.options as INumberPropertyOptions;
          const previewValue = 80;
          const previewValueStr = Number(previewValue).toFixed(options?.precision || 0);
          return (
            <>
              <div className="label">{t('Precision')}</div>
              <div className="value">
                <Select
                  mode={'single'}
                  value={options?.precision}
                  dataSource={NumberPrecisions}
                  autoWidth
                  style={{ width: '100%' }}
                  onChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        precision: c,
                      },
                    });
                  }}
                />
              </div>
              <div className="label">{t('Preview')}</div>
              <div className="value">
                {previewValueStr}
              </div>
            </>
          );
        }
        case CustomPropertyType.Percentage: {
          const options = property.options as IPercentagePropertyOptions;
          const previewValue = 80;
          const previewValueStr = Number(previewValue).toFixed(options?.precision || 0);
          return (
            <>
              <div className="label">{t('Precision')}</div>
              <div className="value">
                <Select
                  mode={'single'}
                  value={options?.precision}
                  dataSource={NumberPrecisions}
                  autoWidth
                  style={{ width: '100%' }}
                  onChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        precision: c,
                      },
                    });
                  }}
                />
              </div>
              <div className="label">{t('Show progressbar')}</div>
              <div className="value">
                <Switch
                  size={'small'}
                  checked={options?.showProgressbar}
                  onChange={c => {
                    setProperty({
                      ...property,
                      options: {
                        ...options,
                        showProgressbar: c,
                      },
                    });
                  }}
                />
              </div>
              <div className="label">{t('Preview')}</div>
              <div className="value">
                {options?.showProgressbar ? (
                  <Progress
                    percent={previewValue}
                    textRender={(p) => `${previewValueStr}%`}
                  />
                ) : `${previewValueStr}%`}
              </div>
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
                  mode={'single'}
                  value={options?.maxValue}
                  dataSource={RatingMaxValueDataSource}
                  autoWidth
                  style={{ width: '100%' }}
                  onChange={c => {
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
        // case CustomPropertyType.Formula:
        //   break;
        // case CustomPropertyType.Multilevel:
        //   break;
      }
    }
    return;
  };

  return (
    <Dialog
      visible={visible}
      className={'custom-property-dialog'}
      onClose={close}
      onCancel={close}
      v2
      closeMode={['close', 'esc']}
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
      {...dialogProps}
    >
      <div
        className={'custom-property-form'}
      >
        <div className="label">{t('Name')}</div>
        <div className="value">
          <Input
            value={property.name}
            onChange={name => setProperty({
              ...property,
              name,
            })}
          />
        </div>
        <div className="label">{t('Type')}</div>
        <div className="value">
          <Popup
            v2
            animation={false}
            trigger={(
              <Button
                text
                type={'primary'}
                className={'type'}
              >
                {property.type == undefined ? t('Please select') : (
                  <>
                    <CustomIcon type={PropertyTypeIconMap[property.type]} size={'small'} />
                    {t(CustomPropertyType[property.type])}
                  </>
                )}
              </Button>
            )}
            triggerType="click"
            placement={'rt'}
            visible={typeGroupsVisible}
            onVisibleChange={v => {
              setTypeGroupsVisible(v);
            }}
          >
            <div className={'grouped-property-types'}>
              {Object.keys(PropertyTypeGroup).map(group => {
                return (
                  <div className={'group'}>
                    <div className={'title'}>{t(group)}</div>
                    <div className="types">
                      {PropertyTypeGroup[group].map(type => {
                        return (
                          <div
                            className={'type'}
                            onClick={() => {
                              setTypeGroupsVisible(false);
                              setProperty({
                                ...property,
                                type,
                              });
                            }}
                          >
                            <CustomIcon type={PropertyTypeIconMap[type]} size={'small'} />
                            {t(CustomPropertyType[type])}
                          </div>
                        );
                      })}
                    </div>
                  </div>
                );
              })}
            </div>
          </Popup>
        </div>
        {renderOptions()}
      </div>
    </Dialog>
  );
};


PropertyDialog.show = (props: IProps) => createPortalOfComponent(PropertyDialog, props);

export default PropertyDialog;
