import { useTranslation } from 'react-i18next';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useUpdateEffect } from 'react-use';
import type { DataPool } from '../../../models';
import type { FilterValueContext } from './models';
import { buildFilterValueContext } from './helpers';
import type { SearchOperation, StandardValueType } from '@/sdk/constants';
import type { IProperty } from '@/components/Property/models';

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
                  value,
                  onChange,
                  property,
                  dataPool,
                }: IProps) => {
  const { t } = useTranslation();
  const [editing, setEditing] = useState(false);
  const ctx = buildFilterValueContext(property, value, dataPool);


  if (!ctx) {
    return (
      <>
        {t('Unsupported type')}
      </>
    );
  }

  return (
    <div className={''}>
      {editing ? (
        ctx.renderValueEditor({
          key: value,
          onChange: v => {
            console.log(v);
            onChange?.(v);
            setEditing(false);
          },
        })
      ) : (
        ctx.renderValueRenderer({
          key: value,
          onClick: () => {
            setEditing(true);
          },
        })
      )}
    </div>
  );
};
