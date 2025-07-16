"use client";

import { useTranslation } from "react-i18next";

type Props = {
  disabled: boolean;
  onDelete?: () => any;
  onDisabledChange?: (disabled: boolean) => any;
  type: "filter" | "group";
};

const TailwindGroupMap = {
  filter: {
    operationGroupHoverBlock: "group-hover/filter-operations:block",
    disableCoverGroup: "group/filter-disable-cover",
    disableCoverGroupHoverBlock: "group-hover/filter-disable-cover:block",
    disableCoverGroupHoverHidden: "group-hover/filter-disable-cover:hidden",
  },
  group: {
    operationGroupHoverBlock: "group-hover/group-operations:block",
    disableCoverGroup: "group/group-disable-cover",
    disableCoverGroupHoverBlock: "group-hover/group-disable-cover:block",
    disableCoverGroupHoverHidden: "group-hover/group-disable-cover:hidden",
  },
};

export default ({ disabled, onDisabledChange, onDelete, type }: Props) => {
  const { t } = useTranslation();

  return <></>;
};
