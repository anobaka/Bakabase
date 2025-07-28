"use client";

import type { PathFilter } from "@/pages/media-library-template/models";

import { useTranslation } from "react-i18next";
import { BsRegex } from "react-icons/bs";
import { IoLayersOutline } from "react-icons/io5";

import { PathFilterFsType } from "@/pages/media-library-template/models";
import { pathFilterFsTypes, PathPositioner } from "@/sdk/constants";
import PathFilterFsTypeBlock from "@/pages/media-library-template/components/PathFilterFsTypeBlock";
import { Chip } from "@/components/bakaui";

type Props = {
  filter: PathFilter;
};
const Demonstrator = ({ filter }: Props) => {
  const { t } = useTranslation();

  const renderPositioner = () => {
    switch (filter.positioner) {
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
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return filter.layer == undefined
          ? t<string>("Not set")
          : filter.layer == 0
            ? t<string>("Directory of media library")
            : t<string>("The {{layer}} layer", { layer: filter.layer });
      case PathPositioner.Regex:
        return filter.regex ?? t<string>("Not set");
      default:
        return t<string>("Not supported");
    }
  };

  const renderFsTypeBlocks = () => {
    if (filter.fsType) {
      return <PathFilterFsTypeBlock type={filter.fsType} />;
    } else {
      return pathFilterFsTypes.map((t) => (
        <PathFilterFsTypeBlock type={t.value} />
      ));
    }
  };

  const renderExtensions = () => {
    const texts = (filter.extensionGroups?.map((x) => x.name) ?? []).concat(
      filter.extensions ?? [],
    );

    return (
      <div className={"flex items-center gap-1"}>
        {texts.map((t) => {
          return (
            <Chip radius={"sm"} size={"sm"} variant={"flat"}>
              {t}
            </Chip>
          );
        })}
      </div>
    );
  };

  return (
    <div className={"flex items-center gap-2"}>
      <div className={"flex items-center gap-1"}>
        {t<string>("Through")}
        {renderPositioner()}
      </div>
      <div className={"flex items-center gap-1"}>{renderLayerOrRegex()}</div>
      <div className={"flex items-center gap-1"}>{renderFsTypeBlocks()}</div>
      {filter.fsType == PathFilterFsType.File && renderExtensions()}
    </div>
  );
};

Demonstrator.displayName = "Demonstrator";

export default Demonstrator;
