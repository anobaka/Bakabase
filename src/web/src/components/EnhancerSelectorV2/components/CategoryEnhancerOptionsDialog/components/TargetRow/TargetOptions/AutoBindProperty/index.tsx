"use client";

import { useTranslation } from "react-i18next";

import { Checkbox, Tooltip } from "@/components/bakaui";

interface IProps {
  autoBindProperty: boolean;
  onChange?: (autoBindProperty: boolean) => void;
}
const AutoBindProperty = ({ autoBindProperty, onChange }: IProps) => {
  const { t } = useTranslation();

  return (
    <>
      <Tooltip
        content={
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
        }
        placement={"left"}
      >
        <Checkbox
          isSelected={autoBindProperty}
          size={"sm"}
          onValueChange={onChange}
        >
          {t<string>("enhancer.targetOptions.autoBind.label")}
        </Checkbox>
      </Tooltip>
    </>
  );
};

AutoBindProperty.displayName = "AutoBindProperty";

export default AutoBindProperty;
