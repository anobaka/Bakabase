"use client";

import { useTranslation } from "react-i18next";

import { Radio, RadioGroup } from "@/components/bakaui";
import { CoverSelectOrder as CoverSelectOrderEnum } from "@/sdk/constants";

type Props = {
  coverSelectOrder?: CoverSelectOrderEnum;
  onChange?: (coverSelectOrder: CoverSelectOrderEnum) => void;
};
const CoverSelectOrder = ({
  coverSelectOrder = CoverSelectOrderEnum.FilenameAscending,
  onChange,
}: Props) => {
  const { t } = useTranslation();

  return (
    <RadioGroup
      orientation="horizontal"
      size={"sm"}
      value={coverSelectOrder.toString()}
      onValueChange={(c) => {
        onChange?.(parseInt(c, 10));
      }}
    >
      {Object.keys(CoverSelectOrderEnum)
        .filter((x) => !Number.isNaN(parseInt(x, 10)))
        .map((x) => {
          return (
            <Radio value={x}>
              {t<string>(`CoverSelectOrder.${CoverSelectOrderEnum[parseInt(x, 10)]}`)}
            </Radio>
          );
        })}
    </RadioGroup>
  );
};

CoverSelectOrder.displayName = "CoverSelectOrder";

export default CoverSelectOrder;
