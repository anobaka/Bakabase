import { useTranslation } from 'react-i18next';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { DataPool } from '../../../models';
import type { FilterValueContext } from './models';
import { buildFilterValueContext } from './helpers';
import type { SearchOperation, StandardValueType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';
import ValueEditor, {
  StringValueEditor,
} from '@/pages/Resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter/FilterValue/ValueEditor';

interface IProps {
  operation?: SearchOperation;
  valueType?: StandardValueType;
  value?: string;
  getDataSource?: () => Promise<{ label: string; value: number }[]>;
  onChange?: (value?: any) => any;
  dataPool?: DataPool;
  property: IProperty;
}

export default ({
                  value: propsValue,
                  onChange,
                  property,
                  dataPool,
                }: IProps) => {
  const { t } = useTranslation();
  const [value, setValue] = useState(propsValue);

  const [tmpValue, setTmpValue] = useState(value);

  const [editing, setEditing] = useState(false);

  useUpdateEffect(() => {
    setValue(propsValue);
    setTmpValue(propsValue);
  }, [propsValue]);

  useUpdateEffect(() => {
    onChange?.(value);
  }, [value]);

  return (
    <div className={''}>
      {editing ? (
        <ValueEditor
          property={property}
          value={value}
          dataPool={dataPool}
          onChange={v => {
            setValue(v);
            setEditing(false);
          }}
        />
        // <StringValueEditor value={value == undefined ? undefined : JSON.parse(value)} onChange={v => { setValue(JSON.stringify(v)); }} />
      ) : (
        <div
          onClick={() => {
            setEditing(true);
          }}
        >1234565</div>
      )}
    </div>
  );
};
