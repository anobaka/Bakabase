import { Button, Dialog, Input, Overlay, Select, Switch } from '@alifd/next';
import type { DialogProps } from '@alifd/next/types/dialog';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { createPortalOfComponent } from '@/components/utils';
import { CustomPropertyType } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import ChoiceList from './components/ChoiceList';
import type { IChoice } from '@/pages/CustomProperty/models';

const { Popup } = Overlay;

interface IProps extends DialogProps {
  value?: CustomPropertyForm;
}

interface ChoicePropertyOptions {
  choices: IChoice[];
  allowAddingNewOptionsWhileChoosing: boolean;
  defaultValue?: string;
}

interface CustomPropertyForm {
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

const PropertyTypeIconMap: Record<CustomPropertyType, string> = {
  [CustomPropertyType.SingleLineText]: 'single-line-text',
  [CustomPropertyType.MultilineText]: 'multiline-text',
  [CustomPropertyType.SingleChoice]: 'radiobox',
  [CustomPropertyType.MultipleChoice]: 'multiple-select',
  // [CustomPropertyType.Multilevel]: 'multi_level',
  [CustomPropertyType.Number]: 'number',
  [CustomPropertyType.Percentage]: 'percentage',
  [CustomPropertyType.Rating]: 'star',
  [CustomPropertyType.Boolean]: 'checkboxchecked',
  [CustomPropertyType.Link]: 'link',
  [CustomPropertyType.Attachment]: 'attachment',
  // [CustomPropertyType.Formula]: 'formula',
};

const PropertyDialog = ({
                          value,
                          ...dialogProps
                        }: IProps) => {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [property, setProperty] = useState<CustomPropertyForm>(value || {
    type: CustomPropertyType.SingleChoice,
  });

  const [typeGroupsVisible, setTypeGroupsVisible] = useState(false);

  const close = () => {
    setVisible(false);
  };

  console.log(6666, 'render');

  const renderOptions = () => {
    if (property.type != undefined) {
      switch (property.type) {
        case CustomPropertyType.SingleLineText:
        case CustomPropertyType.MultilineText:
          break;
        case CustomPropertyType.SingleChoice:
        case CustomPropertyType.MultipleChoice: {
          // list(sortable, color)
          // order by alphabet
          // AllowAddingNewOptionsWhileChoosing
          // default value

          const options = property.options as ChoicePropertyOptions;

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
          // precision
          break;
        }
        case CustomPropertyType.Percentage: {
          // precision
          // show progressbar
          break;
        }
        case CustomPropertyType.Rating: {
          // max value
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
      {...dialogProps}
      v2
    >
      <div className={'custom-property-form'}>
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
