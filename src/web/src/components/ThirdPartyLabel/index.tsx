"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";

interface ThirdPartyLabelProps {
  thirdPartyId: ThirdPartyId;
  /** Forwarded to the icon. Defaults to "sm". */
  size?: "sm" | "md" | "lg";
  /** Hide the text and render only the icon. */
  iconOnly?: boolean;
  className?: string;
}

/**
 * Inline rendering of a ThirdParty's icon + localized name. The single source of
 * truth for "show me this source" so callers don't repeat the
 * t(getEnumKey(...)) dance. Use directly in dropdown items, table cells,
 * subscription summaries, etc. Wrap in a Chip / Tooltip / Button if the
 * site needs it.
 */
const ThirdPartyLabel: React.FC<ThirdPartyLabelProps> = ({
  thirdPartyId,
  size = "sm",
  iconOnly,
  className,
}) => {
  const { t } = useTranslation();
  const name = t<string>(getEnumKey("ThirdPartyId", ThirdPartyId[thirdPartyId]));

  return (
    <span className={`inline-flex items-center gap-2 ${className ?? ""}`}>
      <ThirdPartyIcon size={size} thirdPartyId={thirdPartyId} />
      {!iconOnly && <span>{name}</span>}
    </span>
  );
};

ThirdPartyLabel.displayName = "ThirdPartyLabel";

export default ThirdPartyLabel;
