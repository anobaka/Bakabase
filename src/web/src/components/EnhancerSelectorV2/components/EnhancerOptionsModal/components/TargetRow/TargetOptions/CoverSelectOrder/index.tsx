"use client";

import { useTranslation } from "react-i18next";

import { Radio, RadioGroup } from "@/components/bakaui";
import { CoverSelectOrder } from "@/sdk/constants";

type Props = {
  coverSelectOrder?: CoverSelectOrder;
  onChange?: (coverSelectOrder: CoverSelectOrder) => void;
  isDisabled?: boolean;
};

export default ({ coverSelectOrder, onChange, isDisabled }: Props) => {
  const { t } = useTranslation();

  console.log(coverSelectOrder);

  return (
    <RadioGroup
      isDisabled={isDisabled}
      orientation="horizontal"
      size={"sm"}
      value={coverSelectOrder?.toString()}
      onValueChange={(c) => {
        onChange?.(parseInt(c, 10));
      }}
    >
      {Object.keys(CoverSelectOrder)
        .filter((x) => !Number.isNaN(parseInt(x, 10)))
        .map((x) => {
          return (
            <Radio value={x}>
              {t<string>(`CoverSelectOrder.${CoverSelectOrder[x]}`)}
            </Radio>
          );
        })}
    </RadioGroup>
  );
};
