"use client";

import type { PathPropertyExtractor } from "@/pages/media-library-template/models";

import { useTranslation } from "react-i18next";
import { IoLayersOutline } from "react-icons/io5";
import { BsRegex } from "react-icons/bs";

import {
  PathPositioner,
  PathPropertyExtractorBasePathType,
} from "@/sdk/constants";

type Props = {
  locator: PathPropertyExtractor;
};

export default ({ locator }: Props) => {
  const { t } = useTranslation();

  const renderPositioner = () => {
    switch (locator.positioner) {
      case PathPositioner.Layer:
        return (
          <>
            <IoLayersOutline className={"text-base"} />
            {t<string>("Layer")}
          </>
        );
      case PathPositioner.Regex:
        return (
          <>
            <BsRegex className={"text-base"} />
            {t<string>("Regex")}
          </>
        );
      default:
        return t<string>("Not supported");
    }
  };

  const renderLayerOrRegex = () => {
    switch (locator.positioner) {
      case PathPositioner.Layer:
        const basePathType =
          locator.basePathType ??
          PathPropertyExtractorBasePathType.MediaLibrary;

        if (locator.layer == undefined) {
          return t<string>("Not set");
        }
        if (locator.layer === 0) {
          return t<string>("Self");
        }
        if (locator.layer > 0) {
          return t<string>("The {{n}}th layer after {{basePathType}}", {
            basePathType: t<string>(
              PathPropertyExtractorBasePathType[basePathType],
            ),
            n: locator.layer,
          });
        } else {
          return t<string>("The {{n}}th layer before {{basePathType}}", {
            basePathType: t<string>(
              PathPropertyExtractorBasePathType[basePathType],
            ),
            n: Math.abs(locator.layer),
          });
        }
      case PathPositioner.Regex:
        return locator.regex ?? t<string>("Not set");
      default:
        return t<string>("Not supported");
    }
  };

  return (
    <div className={"flex items-center gap-1"}>
      <div className={"flex items-center gap-1"}>{renderPositioner()}</div>
      <div className={"flex items-center gap-1"}>{renderLayerOrRegex()}</div>
    </div>
  );
};
