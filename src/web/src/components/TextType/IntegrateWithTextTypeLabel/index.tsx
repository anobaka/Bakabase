"use client";

import { useTranslation } from "react-i18next";

import { Chip, Link, Modal, Tooltip } from "@/components/bakaui";
import { WellKnownTextType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  type: WellKnownTextType;
}

/** Marks a field whose value is run through one of the builtin text types, and links to it. */
const IntegrateWithTextTypeLabel = ({ type }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const tooltipContent =
    type === WellKnownTextType.DateTime
      ? t<string>("textType.integration.dateTime.tip")
      : t<string>("textType.integration.default.tip");

  return (
    <Tooltip
      content={
        <div className={"flex items-center gap-1"}>
          {tooltipContent}
          <Link
            className={"active:no-underline cursor-pointer"}
            size={"sm"}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();

              createPortal(Modal, {
                title: t<string>("textType.integration.leaving.title"),
                children: t<string>("textType.integration.leaving.message"),
                defaultVisible: true,
                onOk: () => {
                  window.location.href = "/text";
                },
              });
            }}
          >
            {t<string>("textType.integration.action")}
          </Link>
        </div>
      }
    >
      <Chip radius={"sm"} size={"sm"} variant={"flat"}>
        {t<string>("textType.integration.label")}
      </Chip>
    </Tooltip>
  );
};

IntegrateWithTextTypeLabel.displayName = "IntegrateWithTextTypeLabel";

export default IntegrateWithTextTypeLabel;
