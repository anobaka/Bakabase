"use client";

import type { FC } from "react";
import type { DownloadInfoData } from "../results";

import { useTranslation } from "react-i18next";
import { AiOutlineCopy, AiOutlineLink } from "react-icons/ai";

import { copyParserText } from "../results";

import { Button, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface Props {
  data: DownloadInfoData;
}

const DownloadInfoResultRenderer: FC<Props> = ({ data }) => {
  const { t } = useTranslation();

  const copy = async (value: string) => {
    try {
      await copyParserText(value);
      toast.success(t<string>("postParser.result.copied"));
    } catch {
      toast.danger(t<string>("postParser.result.copyFailed"));
    }
  };

  if (!Array.isArray(data.resources) || data.resources.length === 0) {
    return <div className="text-sm text-default-400">{t("postParser.result.noResources")}</div>;
  }

  return (
    <div className="flex min-w-0 flex-col gap-2">
      {data.title && <p className="text-sm font-medium">{data.title}</p>}
      {data.resources.map((resource, index) => (
        <div key={index} className="flex min-w-0 flex-col gap-1 text-sm">
          {resource.link && (
            <div className="flex min-w-0 items-center gap-1">
              <Button
                className="h-auto min-w-0 max-w-full justify-start px-1 py-1"
                color="primary"
                size="sm"
                startContent={<AiOutlineLink aria-hidden className="shrink-0 text-base" />}
                variant="light"
                onPress={() => BApi.gui.openUrlInDefaultBrowser({ url: resource.link! })}
              >
                <span className="break-all whitespace-normal text-left">{resource.link}</span>
              </Button>
              <Button
                isIconOnly
                aria-label={t<string>("postParser.action.copyLink")}
                className="shrink-0"
                size="sm"
                variant="light"
                onPress={() => copy(resource.link!)}
              >
                <AiOutlineCopy aria-hidden className="text-base" />
              </Button>
            </div>
          )}
          <div className="flex flex-wrap gap-1">
            {resource.code && (
              <Button
                aria-label={t<string>("postParser.action.copyCode")}
                className="h-auto min-h-6 min-w-0 whitespace-normal px-2 py-1 text-xs"
                size="sm"
                startContent={<AiOutlineCopy aria-hidden />}
                variant="flat"
                onPress={() => copy(resource.code!)}
              >
                {t("postParser.label.accessCode")}: {resource.code}
              </Button>
            )}
            {resource.password && (
              <Button
                aria-label={t<string>("postParser.action.copyPassword")}
                className="h-auto min-h-6 min-w-0 whitespace-normal px-2 py-1 text-xs"
                color="warning"
                size="sm"
                startContent={<AiOutlineCopy aria-hidden />}
                variant="flat"
                onPress={() => copy(resource.password!)}
              >
                {t("postParser.label.decompressionPassword")}: {resource.password}
              </Button>
            )}
          </div>
        </div>
      ))}
    </div>
  );
};

export default DownloadInfoResultRenderer;
