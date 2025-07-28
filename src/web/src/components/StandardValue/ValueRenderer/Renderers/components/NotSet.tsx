"use client";

import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

type Props = {
  onClick?: () => any;
};
const NotSet = (props: Props) => {
  const { t } = useTranslation();
  const { onClick } = props;

  if (onClick) {
    return (
      <Button radius={"sm"} size={"sm"} variant={"light"} onClick={onClick}>
        <span className={"opacity-40"}>{t<string>("Click to set")}</span>
      </Button>
    );
  } else {
    return <span className={"opacity-40"}>{t<string>("Not set")}</span>;
  }
};

NotSet.displayName = "NotSet";

export default NotSet;
