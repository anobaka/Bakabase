"use client";

import type {
  SelectedItems,
  SelectProps as NextUISelectProps,
} from "@heroui/react";
import type { Key } from "@react-types/shared";
import type { ReactNode } from "react";

import { Select as HeroSelect, SelectItem as HeroSelectItem } from "@heroui/react";
import { useTranslation } from "react-i18next";

import { Chip } from "@/components/bakaui";

type Data = {
  label?: any;
  value: Key;
  textValue?: string;
  isDisabled?: boolean;
  description?: string;
};

export interface SelectProps extends Omit<NextUISelectProps, "children"> {
  dataSource?: Data[];
  children?: any;
}
const Select: React.FC<SelectProps> = ({ dataSource = [], ...props }) => {
  const { t } = useTranslation();

  const isMultiline = props.selectionMode === "multiple";

  // console.log(props.selectedKeys, dataSource);
  const baseRenderValue =
    props.renderValue ??
    (isMultiline
      ? (v: SelectedItems<Data>) => {
          // console.log(v);
          if (v.length > 0) {
            return (
              <div className={"flex flex-wrap gap-2"}>
                {v.reduce<ReactNode[]>((s, x, i) => {
                  s.push(
                    <Chip radius={"sm"} size={props.size ?? undefined}>
                      {dataSource.find((d) => d.value === x.data?.value)
                        ?.label ?? t<string>("Unknown label")}
                    </Chip>,
                  );

                  return s;
                }, [])}
              </div>
            );
          }

          return undefined;
        }
      : undefined);

  const renderValue = baseRenderValue as unknown as
    | ((items: SelectedItems<object>) => ReactNode)
    | undefined;

  return (
    <HeroSelect
      // aria-label={'Select'}
      isMultiline={isMultiline}
      items={dataSource ?? []}
      renderValue={renderValue}
      {...props}
    >
      {props.children ??
        ((data: Data) => {
          return (
            <HeroSelectItem
              key={data.value}
              aria-label={data.label?.toString()}
              description={data.description}
              textValue={data.textValue ?? data.label?.toString()}
            >
              {data.label}
            </HeroSelectItem>
          );
        })}
    </HeroSelect>
  );
};

Select.displayName = "Select";

export default Select;
