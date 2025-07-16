"use client";

import { Trans, useTranslation } from "react-i18next";
import React, { useCallback } from "react";

import ExternalLink from "@/components/ExternalLink";
import qqGroupImg from "@/assets/qq-group.png";
import { Chip, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  name: string;
  status: "developing" | "deprecated" | "deprecating";
  className?: string;
  url?: string;
};

export default ({ name, status, className, url }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  console.log(name);

  const renderText = useCallback(() => {
    let i18nKey: string;

    switch (status) {
      case "developing":
        i18nKey = "FeatureRequestTip.Developing";
        break;
      case "deprecated":
        i18nKey = "FeatureRequestTip.Deprecated";
        break;
      case "deprecating":
        i18nKey = "FeatureRequestTip.Deprecating";
        break;
    }

    return (
      <Trans i18nKey={i18nKey} values={{ name }}>
        The
        <span className={"font-bold px-1"}>name</span>
        feature is under development. You can urge the author or make
        suggestions on
        <ExternalLink
          className={"px-1"}
          href={"https://github.com/anobaka/InsideWorld"}
        >
          Github
        </ExternalLink>
        or
        <Chip
          // size={'sm'}
          className={"cursor-pointer"}
          classNames={{ content: "px-0" }}
          color={"primary"}
          variant={"light"}
          onClick={() => {
            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Join QQ group"),
              children: <img alt={"Qrcode for qq group"} src={qqGroupImg} />,
              footer: {
                actions: ["ok"],
              },
            });
          }}
        >
          QQ group
        </Chip>
        {/* Children of tooltip in Trans will be wrapped by a <p/> tag, don't know why. */}
        {/* <Tooltip */}
        {/*   placement={'bottom'} */}
        {/*   content={(<img */}
        {/*     src={qqGroupImg} */}
        {/*     alt={'Qrcode for qq group'} */}
        {/*   />)} */}
        {/* > */}
        {/*   QQ group */}
        {/* </Tooltip> */}.
      </Trans>
    );
  }, []);

  return (
    <div className={`italic opacity-80 whitespace-break-spaces ${className}`}>
      {renderText()}
      {url && (
        <>
          {t<string>("For more information, you can visit:")}
          <ExternalLink className={"px-1"} href={url}>
            {url}
          </ExternalLink>
        </>
      )}
    </div>
  );
};
