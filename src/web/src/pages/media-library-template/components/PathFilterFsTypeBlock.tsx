"use client";

import { BsFileEarmark, BsFolder } from "react-icons/bs";
import { useTranslation } from "react-i18next";

import { PathFilterFsType } from "@/pages/media-library-template/models";
const PathFilterFsTypeBlock = ({ type }: { type: PathFilterFsType }) => {
  const { t } = useTranslation();

  switch (type) {
    case PathFilterFsType.File:
      return (
        <div className={"inline-flex items-center gap-1"}>
          <BsFileEarmark />
          {t<string>(PathFilterFsType[type])}
        </div>
      );
    case PathFilterFsType.Directory:
      return (
        <div className={"inline-flex items-center gap-1"}>
          <BsFolder />
          {t<string>(PathFilterFsType[type])}
        </div>
      );
  }
};

PathFilterFsTypeBlock.displayName = "PathFilterFsTypeBlock";

export default PathFilterFsTypeBlock;
