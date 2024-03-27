import { Select } from '@alifd/next';
import React, { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import CustomIcon from '@/components/CustomIcon';
import type { ISearchFormOrderModel } from '@/pages/Resource/models';
import { resourceSearchSortableProperties } from '@/sdk/constants';

const directionDataSource: { label: string; asc: boolean }[] = [{
  label: 'Asc',
  asc: true,
}, {
  label: 'Desc',
  asc: false,
}];

interface IProps {
  value?: ISearchFormOrderModel[];
  onChange?: (value: ISearchFormOrderModel[]) => any;
}

export default ({ value: propsValue, onChange }: IProps) => {
  const { t } = useTranslation();

  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const orderDataSourceRef = useRef(resourceSearchSortableProperties.reduce<{ label: any; value: string }[]>((s, x) => {
    directionDataSource.forEach((y) => {
      s.push({
        label: (
          <div
            style={{ display: 'flex', alignItems: 'center', gap: 5 }}
            title={t(y.label)}
          >
            <CustomIcon type={y.asc ? 'sort-ascending' : 'sort-descending'} size={'small'} />
            {t(x.label)}
          </div>
        ),
        value: `${x.value}-${y.asc}`,
      });
    });

    return s;
  }, []));

  return (
    <Select
      label={t('Orders')}
      autoWidth
      mode={'multiple'}
      style={{
        maxWidth: 500,
        minWidth: 200,
      }}
      showSearch
      dataSource={orderDataSourceRef.current}
      value={(value || []).map((a) => `${a.property}-${a.asc}`)}
      size={'small'}
      onChange={(arr) => {
        // console.log(arr);
        const orderKeys = {};
        const orders: ISearchFormOrderModel[] = [];
        for (let i = arr.length - 1; i >= 0; i--) {
          const vl = arr[i].split('-');
          const o = vl[0];
          if (!(o in orderKeys)) {
            const a = vl[1];
            orders.splice(0, 0, {
              property: parseInt(o, 10),
              asc: a == 'true',
            });
            orderKeys[o] = a;
          }
          // console.log(vl, o, orders);
        }
        setValue(orders);
        onChange?.(orders);
      }}
    />
  );
};
