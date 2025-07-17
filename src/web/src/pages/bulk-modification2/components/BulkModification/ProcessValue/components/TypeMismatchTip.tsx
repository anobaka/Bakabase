"use client";

import { useTranslation } from "react-i18next";
import { WarningOutlined } from "@ant-design/icons";

import { PropertyType } from "@/sdk/constants";
import { Button, Chip, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import TypeConversionRuleOverviewDialog from "@/pages/custom-property/components/TypeConversionRuleOverviewDialog";

type Props = {
  toType: PropertyType;
  fromType: PropertyType;
};

export default (props: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { toType, fromType } = props;

  if (toType == fromType) {
    return null;
  }

  return (
    <Tooltip
      content={
        <div className={"flex flex-col gap-1"}>
          <div>
            {t<string>(
              "We will automatically convert the value from {{fromType}} to {{toType}}.",
              {
                fromType: t<string>(`PropertyType.${PropertyType[fromType]}`),
                toType: t<string>(`PropertyType.${PropertyType[toType]}`),
              },
            )}
          </div>
          <div>
            <Button
              color={"primary"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                createPortal(TypeConversionRuleOverviewDialog, {});
              }}
            >
              {t<string>("You can check type conversion rules here")}
            </Button>
          </div>
        </div>
      }
    >
      <Chip
        className={"ml-2"}
        classNames={{
          content: "flex items-center gap-1",
        }}
        color={"warning"}
        radius={"sm"}
        size={"sm"}
        variant={"light"}
      >
        <WarningOutlined className={"text-base"} />
        {t<string>("Type transformation")}
      </Chip>
    </Tooltip>
  );
};
