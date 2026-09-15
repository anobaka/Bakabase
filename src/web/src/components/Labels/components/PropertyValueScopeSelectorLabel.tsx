"use client";

import { QuestionCircleOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Tooltip } from "@/components/bakaui";
const PropertyValueScopeSelectorLabel = () => {
  const { t } = useTranslation();

  return (
    <div className={"flex items-center gap-1"}>
      {t<string>("property.valueScope.label")}
      <Tooltip
        content={
          <div className={"flex flex-col gap-1"}>
            <div>{t<string>("property.valueScope.description")}</div>
          </div>
        }
      >
        <QuestionCircleOutlined className={"text-base"} />
      </Tooltip>
    </div>
  );
};

PropertyValueScopeSelectorLabel.displayName = "PropertyValueScopeSelectorLabel";

export default PropertyValueScopeSelectorLabel;
