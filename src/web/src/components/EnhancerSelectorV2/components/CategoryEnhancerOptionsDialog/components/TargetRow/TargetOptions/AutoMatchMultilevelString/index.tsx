"use client";

import { useTranslation } from "react-i18next";

import { Checkbox, Tooltip } from "@/components/bakaui";

interface IProps {
  autoMatch?: boolean;
  onChange?: (autoMatch: boolean) => void;
}
const AutoMatchMultilevelString = ({ autoMatch, onChange }: IProps) => {
  const { t } = useTranslation();

  return (
    <>
      <Tooltip
        content={
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
        }
        placement={"left"}
      >
        <Checkbox
          isSelected={autoMatch}
          size={"sm"}
          onValueChange={(c) => {
            onChange?.(c);
          }}
        >
          {t<string>("enhancer.targetOptions.autoMatch.label")}
        </Checkbox>
      </Tooltip>
    </>
  );
};

AutoMatchMultilevelString.displayName = "AutoMatchMultilevelString";

export default AutoMatchMultilevelString;
