"use client";

import type { SearchFormOrderModel } from "@/pages/resource/models";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useUpdateEffect } from "react-use";
import { FaSortAmountDownAlt, FaSortAmountUpAlt } from "react-icons/fa";

import {
  resourceSearchSortableProperties,
  type ResourceSearchSortableProperty,
} from "@/sdk/constants";
import { Button, ButtonGroup, Select, Tooltip } from "@/components/bakaui";

interface IProps extends React.ComponentPropsWithoutRef<any> {
  value?: SearchFormOrderModel[];
  onChange?: (value: SearchFormOrderModel[]) => any;
}
const OrderSelector = ({ value: propsValue, onChange, ...otherProps }: IProps) => {
  const { t } = useTranslation();

  const [value, setValue] = useState<SearchFormOrderModel[] | undefined>(propsValue);

  useUpdateEffect(() => {
    setValue(propsValue);
  }, [propsValue]);

  const propertyDataSource = useMemo(
    () =>
      resourceSearchSortableProperties.map((x) => ({
        label: t<string>(x.label),
        value: x.value.toString(),
      })),
    [t],
  );

  const fallbackProperty = resourceSearchSortableProperties[0]?.value ?? 0;
  const current = (value && value.length > 0 ? value[0] : undefined) as
    | SearchFormOrderModel
    | undefined;
  const currentProperty = (current?.property ?? fallbackProperty) as ResourceSearchSortableProperty;
  const currentAsc = current?.asc ?? false;

  const commit = (next: SearchFormOrderModel[]) => {
    setValue(next);
    onChange?.(next);
  };

  return (
    <div
      className={`flex items-center gap-1 ${otherProps?.className ?? ""}`.trim()}
      style={otherProps?.style}
    >
      <Select
        aria-label={t<string>("Orders")}
        dataSource={propertyDataSource}
        placeholder={t<string>("Select orders")}
        selectedKeys={[currentProperty.toString()]}
        selectionMode={"single"}
        size={"sm"}
        style={{
          maxWidth: 320,
          minWidth: 180,
        }}
        onSelectionChange={(keys) => {
          const first = Array.from((keys as Set<string>) || [])[0];
          const property = parseInt(first as string, 10);

          if (Number.isNaN(property)) {
            return;
          }
          commit([
            {
              property: property as ResourceSearchSortableProperty,
              asc: currentAsc,
            },
          ]);
        }}
        {...(() => {
          const { className, style, ...rest } = otherProps as any;

          return rest;
        })()}
      />
      <ButtonGroup>
        <Tooltip content={t<string>("Asc")}>
          <Button
            isIconOnly
            color={currentAsc ? "primary" : "default"}
            size={"sm"}
            variant={currentAsc ? "solid" : "flat"}
            onPress={() =>
              commit([
                {
                  property: currentProperty,
                  asc: true,
                },
              ])
            }
          >
            <FaSortAmountUpAlt className={"text-base"} />
          </Button>
        </Tooltip>
        <Tooltip content={t<string>("Desc")}>
          <Button
            isIconOnly
            color={!currentAsc ? "primary" : "default"}
            size={"sm"}
            variant={!currentAsc ? "solid" : "flat"}
            onPress={() =>
              commit([
                {
                  property: currentProperty,
                  asc: false,
                },
              ])
            }
          >
            <FaSortAmountDownAlt className={"text-base"} />
          </Button>
        </Tooltip>
      </ButtonGroup>
    </div>
  );
};

OrderSelector.displayName = "OrderSelector";

export default OrderSelector;
