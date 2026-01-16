"use client";

import type { ButtonProps } from "@/components/bakaui";

import React from "react";
import { TbExternalLink } from "react-icons/tb";

import { Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Props = {
  href: string;
} & ButtonProps;
const ExternalLink = ({ href, children, size, ...otherProps }: Props) => {

  return (
    <Button
      color={"primary"}
      href={href}
      variant={"light"}
      size={size}
      {...otherProps}
      onPress={(e) => {
        BApi.gui.openUrlInDefaultBrowser({ url: href });
      }}
    >
      {children}
      <TbExternalLink className={"text-lg"} />
    </Button>
  );
};

ExternalLink.displayName = "ExternalLink";

export default ExternalLink;
