"use client";

import { ApartmentOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Tooltip } from "@/components/bakaui";
const DynamicTargetLabel = () => {
  const { t } = useTranslation();

  return (
    <Tooltip
      content={t<string>(
        "enhancer.target.dynamicLabel.tip",
      )}
    >
      <ApartmentOutlined className={"text-sm"} />
      {/* <Chip */}
      {/*   size={'sm'} */}
      {/*   radius={'sm'} */}
      {/*   className={'flex items-center gap-1'} */}
      {/* > */}
      {/*   {t<string>('Dynamic target')} */}
      {/* </Chip> */}
    </Tooltip>
  );
};

DynamicTargetLabel.displayName = "DynamicTargetLabel";

export default DynamicTargetLabel;
