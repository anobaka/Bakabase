import React from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineAppstore,
  AiOutlineBell,
  AiOutlineCloudDownload,
  AiOutlineDatabase,
  AiOutlineFolderOpen,
  AiOutlineFontSize,
  AiOutlineFunction,
  AiOutlineFileSearch,
  AiOutlineRobot,
} from "react-icons/ai";

import ThirdPartyLabel from "@/components/ThirdPartyLabel";
import { ThirdPartyId } from "@/sdk/constants";

/**
 * Per-group icon + label. Source-based groups reuse ThirdPartyLabel so the workflow editor
 * matches how the same source is rendered in the subscription editor; non-source groups
 * (ai, subscription-generic) get their own simple icon + i18n label.
 */
const GroupLabel: React.FC<{ group: string }> = ({ group }) => {
  const { t } = useTranslation();

  switch (group) {
    case "postParser":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineFileSearch />
          <span>{t("workflow.group.postParser")}</span>
        </span>
      );
    case "pixiv":
      return <ThirdPartyLabel thirdPartyId={ThirdPartyId.Pixiv} />;
    case "exhentai":
      return <ThirdPartyLabel thirdPartyId={ThirdPartyId.ExHentai} />;
    case "ai":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineRobot />
          <span>{t<string>("workflow.group.ai")}</span>
        </span>
      );
    case "subscription":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineFunction />
          <span>{t<string>("workflow.group.subscription")}</span>
        </span>
      );
    case "notification":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineBell />
          <span>{t<string>("workflow.group.notification")}</span>
        </span>
      );
    case "downloader":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineCloudDownload />
          <span>{t<string>("workflow.group.downloader")}</span>
        </span>
      );
    case "fs":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineFolderOpen />
          <span>{t<string>("workflow.group.fs")}</span>
        </span>
      );
    case "text":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineFontSize />
          <span>{t<string>("workflow.group.text")}</span>
        </span>
      );
    case "acquisition":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineCloudDownload />
          <span>{t<string>("workflow.group.acquisition")}</span>
        </span>
      );
    case "collection":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineAppstore />
          <span>{t<string>("workflow.group.collection")}</span>
        </span>
      );
    case "resource":
      return (
        <span className="inline-flex items-center gap-2">
          <AiOutlineDatabase />
          <span>{t<string>("workflow.group.resource")}</span>
        </span>
      );
    default:
      // Unknown group → fall back to the raw tag rather than hide the activity.
      return <span className="text-default-500">{group}</span>;
  }
};

export default GroupLabel;
