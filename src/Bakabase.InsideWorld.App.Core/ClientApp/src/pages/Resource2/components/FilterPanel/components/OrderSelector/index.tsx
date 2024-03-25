import { Select } from '@alifd/next';
import React, { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import type { ResourceSearchOrder } from '@/sdk/constants';
import { resourceSearchOrders } from '@/sdk/constants';
import CustomIcon from '@/components/CustomIcon';

const directionDataSource: { label: string; asc: boolean }[] = [{
  label: 'Asc',
  asc: true,
}, {
  label: 'Desc',
  asc: false,
}];

export default () => {
  const { t } = useTranslation();

  const orderDataSourceRef = useRef(resourceSearchOrders.reduce<{ label: any; value: string }[]>((s, x) => {
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
      // value={(searchForm.orders || []).map((a) => `${a.order}-${a.asc}`)}
      size={'small'}
      onChange={(arr) => {
        // console.log(arr);
        const orderKeys = {};
        const orders: {order: ResourceSearchOrder; asc: boolean}[] = [];
        for (let i = arr.length - 1; i >= 0; i--) {
          const vl = arr[i].split('-');
          const o = vl[0];
          if (!(o in orderKeys)) {
            const a = vl[1];
            orders.splice(0, 0, {
              order: parseInt(o, 10),
              asc: a == 'true',
            });
            orderKeys[o] = a;
          }
          // console.log(vl, o, orders);
        }
        // search({
        //   ...searchForm,
        //   orders,
        // });
      }}
    />
  );
};
