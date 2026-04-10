"use client";

import type { FC } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCopy, AiOutlineLink } from "react-icons/ai";

import { Button, Chip, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface DownloadResource {
  link?: string;
  code?: string | null;
  password?: string | null;
}

interface DownloadInfoData {
  title?: string;
  resources?: DownloadResource[] | null;
}

interface DownloadInfoResultRendererProps {
  data: DownloadInfoData;
}

const copyToClipboard = async (text: string, label: string, t: (key: string) => string) => {
  try {
    await navigator.clipboard.writeText(text);
    toast.success(t("postParser.result.copied"));
  } catch {
    // Fallback
    const textarea = document.createElement("textarea");
    textarea.value = text;
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand("copy");
    document.body.removeChild(textarea);
    toast.success(t("postParser.result.copied"));
  }
};

const DownloadInfoResultRenderer: FC<DownloadInfoResultRendererProps> = ({ data }) => {
  const { t } = useTranslation();

  if (!data.resources || data.resources.length === 0) {
    return (
      <div className="text-default-400 text-sm">
        {t("postParser.result.noResources")}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      {data.resources.map((resource, index) => (
        <div
          key={index}
          className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm"
        >
          {resource.link && (
            <Button
              size="sm"
              variant="light"
              color="primary"
              className="max-w-[300px] truncate"
              startContent={<AiOutlineLink className="text-base shrink-0" />}
              onPress={() => {
                BApi.gui.openUrlInDefaultBrowser({ url: resource.link! });
              }}
            >
              <span className="truncate">{resource.link}</span>
            </Button>
          )}
          {resource.code && (
            <Chip
              size="sm"
              variant="flat"
              className="cursor-pointer"
              startContent={<AiOutlineCopy className="text-xs ml-1" />}
              onClick={() => copyToClipboard(resource.code!, "code", t)}
            >
              {t("postParser.label.accessCode")}: {resource.code}
            </Chip>
          )}
          {resource.password && (
            <Chip
              size="sm"
              variant="flat"
              color="warning"
              className="cursor-pointer"
              startContent={<AiOutlineCopy className="text-xs ml-1" />}
              onClick={() => copyToClipboard(resource.password!, "password", t)}
            >
              {t("postParser.label.decompressionPassword")}: {resource.password}
            </Chip>
          )}
        </div>
      ))}
    </div>
  );
};

export default DownloadInfoResultRenderer;
