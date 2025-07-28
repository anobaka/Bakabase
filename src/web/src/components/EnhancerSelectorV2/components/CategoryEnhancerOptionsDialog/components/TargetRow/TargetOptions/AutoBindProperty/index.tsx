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
                "If this option is checked, a property with the same name and type of the target will be bound automatically.",
              )}
            </div>
            <div>
              {t<string>(
                "If there isn't such a property, a new property will be created and bound to this target.",
              )}
            </div>
            <div>
              {t<string>(
                "If this option is checked in default options of a dynamic target, all unlisted dynamic targets will be bound to properties of the same type with the same name.",
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
          {t<string>("Auto bind property")}
        </Checkbox>
      </Tooltip>
    </>
  );
};

AutoBindProperty.displayName = "AutoBindProperty";

export default AutoBindProperty;
