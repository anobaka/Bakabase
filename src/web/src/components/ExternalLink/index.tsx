"use client";

import type { LinkProps } from "@heroui/react";

import React from "react";
import { TbExternalLink } from "react-icons/tb";
import { Link } from "@heroui/react";

import BApi from "@/sdk/BApi";

type Props = {
  href: string;
} & Omit<LinkProps, "href" | "onPress">;

const ExternalLink = ({ href, children, size, ...otherProps }: Props) => {
  return (
    <Link
      color="primary"
      size={size}
      className="cursor-pointer gap-1"
      {...otherProps}
      onPress={(e) => {
        BApi.gui.openUrlInDefaultBrowser({ url: href });
      }}
    >
      {children}
      <TbExternalLink className="text-sm" />
    </Link>
  );
};

ExternalLink.displayName = "ExternalLink";

export default ExternalLink;
