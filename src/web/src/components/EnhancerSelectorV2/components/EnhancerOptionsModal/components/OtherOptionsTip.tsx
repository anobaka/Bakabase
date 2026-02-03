"use client";

import { useTranslation } from "react-i18next";
import { QuestionCircleOutlined } from "@ant-design/icons";

import { Popover } from "@/components/bakaui";
const OtherOptionsTip = () => {
  const { t } = useTranslation();

  return (
    <Popover trigger={<QuestionCircleOutlined className={"text-base"} />}>
      <div className={"flex flex-col gap-1"}>
        <div>
          <div className={"font-bold text-base"}>
            {t<string>("enhancer.targetOptions.autoBind.label")}
          </div>
          <div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoBind.description",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoBind.createTip",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoBind.dynamicTip",
              )}
            </div>
          </div>
        </div>
        <div>
          <div className={"font-bold text-base"}>
            {t<string>("enhancer.targetOptions.autoMatch.label")}
          </div>
          <div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoMatch.description",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoMatch.checkedBehavior",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoMatch.example",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoMatch.exampleResult",
              )}
            </div>
            <div>
              {t<string>(
                "enhancer.targetOptions.autoMatch.warning",
              )}
            </div>
          </div>
        </div>
      </div>
    </Popover>
  );
};

OtherOptionsTip.displayName = "OtherOptionsTip";

export default OtherOptionsTip;
