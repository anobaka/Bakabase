"use client";

import type { LinkProps } from "@heroui/react";

import React from "react";
import { TbExternalLink } from "react-icons/tb";
import { Link } from "@heroui/react";

import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { browserExternalUrl, openExternalUrl } from "@/utils/openExternalUrl";

type Props = {
  href: string;
} & Omit<LinkProps, "href" | "onPress">;

const ExternalLink = ({ href, children, size, ...otherProps }: Props) => {
  const inBrowser = useRemoteAccessStore((state) => state.clientMode === ClientMode.RemoteBrowser);
  const browserHref = inBrowser ? browserExternalUrl(href) : undefined;

  return (
    <Link
      className="cursor-pointer gap-1"
      color="primary"
      size={size}
      {...otherProps}
      href={browserHref}
      isDisabled={otherProps.isDisabled || (inBrowser && !browserHref)}
      rel={inBrowser ? "noopener noreferrer" : otherProps.rel}
      target={inBrowser ? "_blank" : otherProps.target}
      onPress={inBrowser ? undefined : () => openExternalUrl(href)}
    >
      {children}
      <TbExternalLink className="text-sm" />
    </Link>
  );
};

ExternalLink.displayName = "ExternalLink";

export default ExternalLink;
