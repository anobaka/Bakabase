"use client";

import type { SearchFormOrderModel } from "@/pages/resource/models";

import React, { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";
import { FaSortAmountDownAlt, FaSortAmountUpAlt } from "react-icons/fa";

import {
  resourceSearchSortableProperties,
  type ResourceSearchSortableProperty,
} from "@/sdk/constants";
import { Select } from "@/components/bakaui";

const directionDataSource: { label: string; asc: boolean }[] = [
  {
    label: "Asc",
    asc: true,
  },
  {
    label: "Desc",
    asc: false,
  },
];

interface IProps extends React.ComponentPropsWithoutRef<any> {
  value?: SearchFormOrderModel[];
  onChange?: (value: SearchFormOrderModel[]) => any;
}

export default ({ value: propsValue, onChange, ...otherProps }: IProps) => {
  const { t } = useTranslation();

  const [value, setValue] = useState(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const orderDataSourceRef = useRef(
    resourceSearchSortableProperties.reduce<
      { label: any; value: string; textValue: string }[]
    >((s, x) => {
      directionDataSource.forEach((y) => {
        s.push({
          label: (
            <div
              style={{
                display: "flex",
                alignItems: "center",
                gap: 5,
              }}
              title={t<string>(y.label)}
            >
              {y.asc ? (
                <FaSortAmountUpAlt className={"text-lg"} />
              ) : (
                <FaSortAmountDownAlt className={"text-lg"} />
              )}
              {t<string>(x.label)}
            </div>
          ),
          value: `${x.value}-${y.asc}`,
          textValue: `${t<string>(x.label)}${y.asc ? "⬆️" : "⬇️"}`,
        });
      });

      return s;
    }, []),
  );

  return (
    <div>
      <Select
        aria-label={t<string>('Orders')}
        selectedKeys={(value || []).map((a) => `${a.property}-${a.asc}`)}
        selectionMode={'single'}
        size={'sm'}
        style={{
          maxWidth: 500,
          minWidth: 200,
        }}
        onSelectionChange={(arr) => {
          const set = arr as Set<string>;
          const orderAscMap: {[key in ResourceSearchSortableProperty]?: boolean} = {};
          for (const v of set.values()) {
            const vl = v.split('-');
            orderAscMap[parseInt(vl[0], 10)] = vl[1] === 'true';
          }

          const orders: SearchFormOrderModel[] = [];
          for (const k in orderAscMap) {
            orders.push({
              property: parseInt(k, 10),
              asc: orderAscMap[k],
            });
          }

          setValue(orders);
          onChange?.(orders);
        }}
        placeholder={t<string>('Select orders')}
        // label={t<string>('Order')}
        dataSource={orderDataSourceRef.current}
        {...otherProps}
      />
    </div>
  );
};
