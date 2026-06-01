import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineBell, AiOutlineCloudDownload, AiOutlineFunction, AiOutlineRobot } from "react-icons/ai";

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
    default:
      // Unknown group → fall back to the raw tag rather than hide the activity.
      return <span className="text-default-500">{group}</span>;
  }
};

export default GroupLabel;
