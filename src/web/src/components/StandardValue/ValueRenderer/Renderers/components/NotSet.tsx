"use client";

import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

type Props = {
  onClick?: () => any;
  size?: "sm" | "md" | "lg";
};
const NotSet = (props: Props) => {
  const { t } = useTranslation();
  const { onClick, size } = props;

  if (onClick) {
    return (
      <Button radius={"sm"} size={size} variant={"light"} onClick={onClick}>
        <span className={"opacity-40"}>{t<string>("common.label.clickToSet")}</span>
      </Button>
    );
  } else {
    return <span className={"opacity-40"}>{t<string>("common.label.notSet")}</span>;
  }
};

NotSet.displayName = "NotSet";

export default NotSet;
