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

  const orderDataSourceRef = useRef(resourceSearchSortableProperties.reduce<{ label: any; value: string }[]>((s, x) => {
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
      selectionMode={'multiple'}
      style={{
        maxWidth: 500,
        minWidth: 200,
      }}
      label={t('Order')}
      dataSource={orderDataSourceRef.current}
      selectedKeys={(value || []).map((a) => `${a.property}-${a.asc}`)}
      size={'sm'}
      onSelectionChange={(arr) => {
        const orderAscMap: {[key in ResourceSearchSortableProperty]?: boolean} = {};
        for (const v of arr.values()) {
          const vl = (v as string).split('-');
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
  );
};
