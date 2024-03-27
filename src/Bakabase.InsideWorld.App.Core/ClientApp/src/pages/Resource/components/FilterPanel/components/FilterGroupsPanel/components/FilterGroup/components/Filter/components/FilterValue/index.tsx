import { Button, Checkbox, DatePicker2, Input, NumberPicker, Select, TimePicker2 } from '@alifd/next';
import { useTranslation } from 'react-i18next';
import { useEffect, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import moment from 'moment';
import { toast } from 'react-toastify';
import styles from './index.module.scss';
import { SearchOperation, StandardValueType } from '@/sdk/constants';

interface IProps {
  operation?: SearchOperation;
  valueType?: StandardValueType;
  value?: any;
  getDataSource?: () => Promise<{ label: string; value: number }[]>;
  onChange?: (value?: any) => any;
}

export default ({
                  operation,
                  value: propsValue,
                  valueType,
                  getDataSource,
                  onChange,
                }: IProps) => {
  const { t } = useTranslation();
  const [value, setValue] = useState(propsValue);

  const [tmpValue, setTmpValue] = useState(value);

  const [editing, setEditing] = useState(false);
  const [dataSource, setDataSource] = useState<{ label: string; value: number }[]>([]);

  useEffect(() => {
    if (getDataSource) {
      getDataSource().then(setDataSource);
    }
  }, [getDataSource]);

  useUpdateEffect(() => {
    setValue(propsValue);
    setTmpValue(propsValue);
  }, [propsValue]);

  useUpdateEffect(() => {
    onChange?.(value);
  }, [value]);

  const renderValue = () => {
    let innerValue: any;
    switch (valueType!) {
      case StandardValueType.SingleLineText:
      case StandardValueType.MultilineText:
      case StandardValueType.Link:
        innerValue = value;
        break;
      case StandardValueType.SingleChoice:
      case StandardValueType.MultipleChoice:
      case StandardValueType.Multilevel: {
        const v = value as number;
        let values: number[];
        if (v == undefined) {
          values = value as number[] ?? [];
        } else {
          values = [v];
        }
        if (values.length > 0) {
          innerValue = values.map(v => dataSource.find(x => x.value == v)?.label).filter(x => x != undefined).join(',');
        }
        break;
      }
      case StandardValueType.Number:
      case StandardValueType.Percentage:
      case StandardValueType.Rating:
        innerValue = value;
        break;
      case StandardValueType.Boolean: {
        const v = value as boolean;
        if (v != undefined) {
          innerValue = t(v ? 'Yes' : 'No');
        }
        break;
      }
      case StandardValueType.Attachment:
      case StandardValueType.Formula:
        return;
      case StandardValueType.Date: {
        const v = value as string;
        if (v != undefined) {
          innerValue = moment(v).format('YYYY-MM-DD');
        }
        break;
      }
      case StandardValueType.DateTime: {
        const v = value as string;
        if (v != undefined) {
          innerValue = moment(v).format('YYYY-MM-DD HH:mm:ss');
        }
        break;
      }
      case StandardValueType.Time: {
        const v = value as string;
        if (v != undefined) {
          innerValue = moment(v, ['HH:mm:ss', 'HH:mm:ss.SSS']).format('HH:mm:ss');
        }
        break;
      }
    }

    const disabled = operation == undefined;

    return (
      <Button
        type={'primary'}
        text
        size={'small'}
        disabled={disabled}
        onClick={() => {
          setEditing(true);
        }}
      >
        {innerValue ?? t('Value')}
      </Button>
    );
  };

  const renderEditingValue = () => {
    switch (valueType!) {
      case StandardValueType.SingleLineText:
      case StandardValueType.MultilineText:
      case StandardValueType.Link: {
        switch (operation!) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
          case SearchOperation.Contains:
          case SearchOperation.NotContains:
          case SearchOperation.StartsWith:
          case SearchOperation.NotStartsWith:
          case SearchOperation.EndsWith:
          case SearchOperation.NotEndsWith:
          case SearchOperation.Matches:
          case SearchOperation.NotMatches:
            return editing ? (
              <Input
                size={'small'}
                value={tmpValue}
                onChange={v => {
                  setTmpValue(v);
                }}
                onKeyDown={e => {
                  if (e.key == 'Enter') {
                    setValue(tmpValue);
                    setEditing(false);
                  }
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
              />
            ) : value;
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            return;
        }
        break;
      }
      case StandardValueType.SingleChoice: {
        switch (operation) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
          case SearchOperation.In:
          case SearchOperation.NotIn: {
            return (
              <Select
                dataSource={dataSource}
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v);
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
                mode={operation == SearchOperation.In || operation == SearchOperation.NotIn ? 'multiple' : undefined}
              />
            );
          }
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
      case StandardValueType.MultipleChoice:
        switch (operation) {
          case SearchOperation.Contains:
          case SearchOperation.NotContains: {
            return (
              <Select
                dataSource={dataSource}
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v);
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
                mode={'multiple'}
              />
            );
          }
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      case StandardValueType.Number:
      case StandardValueType.Percentage:
      case StandardValueType.Rating:
        switch (operation) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
          case SearchOperation.GreaterThan:
          case SearchOperation.LessThan:
          case SearchOperation.GreaterThanOrEquals:
          case SearchOperation.LessThanOrEquals: {
            return (
              <NumberPicker
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v);
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
              />
            );
          }
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      case StandardValueType.Boolean:
        switch (operation) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
            return (
              <Checkbox
                checked={tmpValue}
                onChange={(v) => {
                  setTmpValue(v);
                  setValue(v);
                }}
              />
            );
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      case StandardValueType.Attachment: {
        switch (operation) {
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
      case StandardValueType.Date:
      case StandardValueType.DateTime: {
        switch (operation) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
          case SearchOperation.GreaterThan:
          case SearchOperation.LessThan:
          case SearchOperation.GreaterThanOrEquals:
          case SearchOperation.LessThanOrEquals:
            return (
              <DatePicker2
                showTime={valueType == StandardValueType.DateTime}
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v.format(`YYYY-MM-DD${valueType == StandardValueType.DateTime ? ' HH:mm:ss' : ''}`));
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
              />
            );
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
      case StandardValueType.Time: {
        switch (operation) {
          case SearchOperation.Equals:
          case SearchOperation.NotEquals:
          case SearchOperation.GreaterThan:
          case SearchOperation.LessThan:
          case SearchOperation.GreaterThanOrEquals:
          case SearchOperation.LessThanOrEquals:
            return (
              <TimePicker2
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v);
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
              />
            );
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
      case StandardValueType.Formula: {
        switch (operation) {
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
      case StandardValueType.Multilevel: {
        switch (operation) {
          case SearchOperation.Contains:
          case SearchOperation.NotContains: {
            return (
              <Select
                dataSource={dataSource}
                value={tmpValue}
                size={'small'}
                onChange={v => {
                  setTmpValue(v);
                }}
                onBlur={() => {
                  setValue(tmpValue);
                  setEditing(false);
                }}
                mode={'multiple'}
              />
            );
          }
          case SearchOperation.IsNull:
          case SearchOperation.IsNotNull:
            break;
        }
        break;
      }
    }
    return;
  };

  return (
    <div className={styles.value}>
      {editing ? renderEditingValue() : renderValue()}
    </div>
  );
};
