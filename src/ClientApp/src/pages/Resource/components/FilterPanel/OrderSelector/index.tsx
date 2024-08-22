import React, { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useUpdateEffect } from 'react-use';
import { SortAscendingOutlined } from '@ant-design/icons';
import CustomIcon from '@/components/CustomIcon';
import type { ISearchFormOrderModel } from '@/pages/Resource/models';
import { resourceSearchSortableProperties, type ResourceSearchSortableProperty } from '@/sdk/constants';
import type { SelectProps } from '@/components/bakaui';
import { Select } from '@/components/bakaui';

const directionDataSource: { label: string; asc: boolean }[] = [{
  label: 'Asc',
  asc: true,
}, {
  label: 'Desc',
  asc: false,
}];

interface IProps extends React.ComponentPropsWithoutRef<any> {
  value?: ISearchFormOrderModel[];
  onChange?: (value: ISearchFormOrderModel[]) => any;
}

export default ({
                  value: propsValue,
                  onChange,
  ...otherProps
                }: IProps) => {
  const { t } = useTranslation();

  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const orderDataSourceRef = useRef(resourceSearchSortableProperties.reduce<{ label: any; value: string; textValue: string }[]>((s, x) => {
    directionDataSource.forEach((y) => {
      s.push({
        label: (
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 5,
            }}
            title={t(y.label)}
          >
            <CustomIcon type={y.asc ? 'sort-ascending' : 'sort-descending'} className={'text-lg'} />
            {t(x.label)}
          </div>
        ),
        value: `${x.value}-${y.asc}`,
        textValue: `${x.label}${t(y.asc ? 'Asc' : 'Desc')}`,
      });
    });

    return s;
  }, []));

  return (
    <div>
      <Select
        aria-label={t('Orders')}
        selectionMode={'multiple'}
        style={{
          maxWidth: 500,
          minWidth: 200,
        }}
        placeholder={t('Select orders')}
        // label={t('Order')}
        dataSource={orderDataSourceRef.current}
        selectedKeys={(value || []).map((a) => `${a.property}-${a.asc}`)}
        size={'sm'}
        onSelectionChange={(arr) => {
          const set = arr as Set<string>;
          const orderAscMap: {[key in ResourceSearchSortableProperty]?: boolean} = {};
          for (const v of set.values()) {
            const vl = v.split('-');
            orderAscMap[parseInt(vl[0], 10)] = vl[1] === 'true';
          }

          const orders: ISearchFormOrderModel[] = [];
          for (const k in orderAscMap) {
            orders.push({
              property: parseInt(k, 10),
              asc: orderAscMap[k],
            });
          }

          setValue(orders);
          onChange?.(orders);
        }}
        {...otherProps}

      />
    </div>
  );
};
