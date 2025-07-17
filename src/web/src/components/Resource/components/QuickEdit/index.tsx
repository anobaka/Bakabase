"use client";

import { EditOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Button, Tooltip } from "@/components/bakaui";

// todo: design
export default () => {
  const { t } = useTranslation();

  return (
    <div>
      <Tooltip
        content={
          <div>
            <div>{t<string>("Edit property values quickly")}</div>
            <div>
              {t<string>(
                "You can hover over the property name in the resource details to set the property as quickly editable",
              )}
            </div>
          </div>
        }
      >
        <Button isIconOnly radius={"sm"} size={"sm"} variant={"light"}>
          <EditOutlined className={"text-base"} />
        </Button>
      </Tooltip>
    </div>
  );
};
