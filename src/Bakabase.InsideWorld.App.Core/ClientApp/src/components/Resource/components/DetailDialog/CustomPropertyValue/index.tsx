import { Checkbox, Input, Progress, Rating, Select } from '@alifd/next';
import React, { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import type {
  IChoicePropertyOptions,
  ICustomProperty,
  INumberPropertyOptions,
  IPercentagePropertyOptions,
  IRatingPropertyOptions,
} from '@/pages/CustomProperty/models';
import { CustomPropertyType } from '@/sdk/constants';
import ExternalLink from '@/components/ExternalLink';
import ClickableIcon from '@/components/ClickableIcon';

import './index.scss';
import BApi from '@/sdk/BApi';

interface IProps {
  resourceId: number;
  property: ICustomProperty;
  value?: any;
  onSaved?: () => any;
}

export default ({ resourceId, property, value: propsValue, onSaved }: IProps) => {
  const { t } = useTranslation();
  const [value, setValue] = useState(propsValue);
  const [editing, setEditing] = useState(false);

  useUpdateEffect(() => {
  }, [editing]);

  const renderEditingValue = () => {
    switch (property.type) {
      case CustomPropertyType.SingleLineText:
        return (
          <Input
            value={value}
            onChange={v => setValue(v)}
          />
        );
      case CustomPropertyType.MultilineText:
        break;
      case CustomPropertyType.SingleChoice:
      case CustomPropertyType.MultipleChoice:
        return (
          <Select
            tabIndex={0}
            autoFocus
            size={'small'}
            showSearch
            hasClear
            value={value}
            onChange={
              v => setValue(v)
            }
            dataSource={(property.options as IChoicePropertyOptions)?.choices?.map(x => ({
              label: x.value,
              value: x.id,
            }))}
          />
        );
        break;
      case CustomPropertyType.Number:
        break;
      case CustomPropertyType.Percentage:
        break;
      case CustomPropertyType.Rating:
        break;
      case CustomPropertyType.Boolean:
        break;
      case CustomPropertyType.Link:
        break;
      case CustomPropertyType.Attachment:
        break;
    }
    return;
  };

  const renderReadonlyValue = () => {
    switch (property.type) {
      case CustomPropertyType.SingleLineText:
      case CustomPropertyType.MultilineText:
        return value;
      case CustomPropertyType.SingleChoice: {
        const choices = (property.options as IChoicePropertyOptions)?.choices;
        const id = value as string;
        return choices?.find(x => x.id == id)?.value;
      }
      case CustomPropertyType.MultipleChoice: {
        const choices = (property.options as IChoicePropertyOptions)?.choices;
        const ids = value as string[];
        return ids?.map(x => choices?.find(c => c.id == x)?.value)?.join(', ');
      }
      case CustomPropertyType.Number: {
        const options = property.options as INumberPropertyOptions;
        return (value as number)?.toFixed(options.precision);
      }
      case CustomPropertyType.Percentage: {
        const options = property.options as IPercentagePropertyOptions;
        const percent = (value as number);
        const valueStr = percent?.toFixed(options?.precision ?? 0);
        if (options != undefined) {
          if (options.showProgressbar) {
            return (
              <Progress percent={percent} textRender={p => valueStr} />
            );
          } else {
            return valueStr;
          }
        }
        return;
      }
      case CustomPropertyType.Rating: {
        const options = property.options as IRatingPropertyOptions;
        const rating = (value as number);
        const maxValue = options?.maxValue ?? 5;
        return (
          <Rating value={rating} count={maxValue} />
        );
      }
      case CustomPropertyType.Boolean:
        return (
          <Checkbox checked={value} />
        );
      case CustomPropertyType.Link:
        return value == undefined ? undefined : (
          <ExternalLink to={value.url}>{value.text}</ExternalLink>
        );
      case CustomPropertyType.Attachment:
        break;
    }
  };

  const renderValueContainer = () => {
    const classNames = ['value'];
    let child: any;
    if (editing) {
      child = renderEditingValue();
    } else {
      child = renderReadonlyValue();
      if (child == undefined) {
        child = (
          <ClickableIcon
            type={'edit-square'}
            size={'small'}
            colorType={'normal'}
          />
        );
        classNames.push('not-set');
      }
    }
    return (
      <div
        className={classNames.join(' ')}
      >{child}</div>
    );
  };

  return (
    <div
      className={'resource-detail-dialog-custom-property-value'}
      onClick={() => {
        if (!editing) {
          setEditing(true);
        }
      }}
      onBlur={() => {
        // alert('blur12345');
        if (editing) {
          BApi.resource.putResourceCustomPropertyValue(resourceId, property.id, { value: JSON.stringify(value) }).then(r => {
            if (!r.code) {
              setEditing(false);
              onSaved?.();
            }
          });
        }
      }}
    >
      {renderValueContainer()}
      {/* <div className="opts"> */}
      {/*  {editing ? ( */}
      {/*    <> */}
      {/*      /!* <ClickableIcon *!/ */}
      {/*      /!*  useInBuildIcon *!/ */}
      {/*      /!*  colorType={'normal'} *!/ */}
      {/*      /!*  // size={'small'} *!/ */}
      {/*      /!*  type={'select'} *!/ */}
      {/*      /!*  className={'submit'} *!/ */}
      {/*      /!*  onClick={() => { *!/ */}
      {/*      /!*  }} *!/ */}
      {/*      /!* /> *!/ */}
      {/*      /!* <ClickableIcon *!/ */}
      {/*      /!*  useInBuildIcon *!/ */}
      {/*      /!*  type={'close'} *!/ */}
      {/*      /!*  colorType={'danger'} *!/ */}
      {/*      /!*  // size={'small'} *!/ */}
      {/*      /!*  onClick={() => { *!/ */}
      {/*      /!*    setEditing(false); *!/ */}
      {/*      /!*  }} *!/ */}
      {/*      /!*  warning *!/ */}
      {/*      /!*  className={'cancel'} *!/ */}
      {/*      /!* /> *!/ */}
      {/*    </> */}
      {/*  ) : ( */}
      {/*    <ClickableIcon */}
      {/*      colorType={'normal'} */}
      {/*      type={'edit-square'} */}
      {/*      size={'small'} */}
      {/*      onClick={() => { */}
      {/*        setEditing(true); */}
      {/*      }} */}
      {/*    /> */}
      {/*  )} */}
      {/* </div> */}
    </div>
  );
};
