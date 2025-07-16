"use client";

import { useTranslation } from "react-i18next";

import { Chip, Link, Modal, Tooltip } from "@/components/bakaui";
import { SpecialTextType } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  type: SpecialTextType;
}

export default ({ type }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  let tooltipContent = "";

  switch (type) {
    case SpecialTextType.DateTime:
      tooltipContent = t<string>(
        "Data will be attempted to be converted into date&time data according to relevant conversions.",
      );
      break;
    case SpecialTextType.Useless:
    case SpecialTextType.Language:
    case SpecialTextType.Wrapper:
    case SpecialTextType.Standardization:
    case SpecialTextType.Volume:
    case SpecialTextType.Trim:
      tooltipContent = t<string>("Data will be processed with special texts.");
      break;
    default:
      break;
  }

  return (
    <Tooltip
      content={
        <div className={"flex items-center gap-1"}>
          {tooltipContent}
          <Link
            className={"active:no-underline cursor-pointer"}
            // href={'#'}
            size={"sm"}
            onClick={(e) => {
              e.preventDefault();
              e.stopPropagation();

              createPortal(Modal, {
                title: t<string>("We are leaving current page"),
                children: t<string>("Are you sure?"),
                defaultVisible: true,
                onOk: () => {
                  window.location.href = "/text";
                },
              });
            }}
          >
            {t<string>("Click to check special texts")}
          </Link>
        </div>
      }
    >
      <Chip radius={"sm"} size={"sm"} variant={"flat"}>
        {t<string>("Integrate with special text")}
      </Chip>
    </Tooltip>
  );
};
