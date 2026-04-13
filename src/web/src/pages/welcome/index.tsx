"use client";

import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { Button, Chip, Modal } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import LanguageSwitcher from "@/components/LanguageSwitcher";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const WelcomePage = () => {
  const [version, setVersion] = useState<string>();
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    BApi.app.getAppInfo().then((a) => {
      setVersion(a.data?.coreVersion ?? "0.0.0");
    });
  }, []);

  return (
    <div className={"flex items-center justify-center w-screen h-screen"}>
      <div className={"flex flex-col gap-4 items-center"}>
        <div className={"flex justify-center"}>
          <span className={"text-5xl relative"}>
            Bakabase
            <Chip
              className={"r-0 bottom-[4px] absolute"}
              color={"success"}
              variant={"light"}
            >
              v{version}
            </Chip>
          </span>
        </div>
        <div className={"flex items-center gap-2"}>
          <span className={"text-default-500"}>{t<string>("welcome.label.systemLanguage")}</span>
          <LanguageSwitcher />
        </div>
        <div className={"flex items-center gap-1"}>
          {t<string>("welcome.info.termsNotice")}
          <Button
            color={"success"}
            radius={"sm"}
            variant={"light"}
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                size: "5xl",
                title: t<string>("welcome.label.termsAndConditions"),
                footer: { actions: ["cancel"] },
                children: (
                  <div className={"whitespace-pre-wrap"}>
                    {t<string>("welcome.info.clarityNotice")}
                  </div>
                ),
              });
            }}
          >
            {t<string>("welcome.action.clickToCheck")}
          </Button>
        </div>
        <Button
          color={"primary"}
          onPress={() => {
            BApi.app.acceptTerms().then((a) => {
              navigate("/");
            });
          }}
        >
          {t<string>("welcome.action.acceptAndStart")}
        </Button>
      </div>
    </div>
  );
};

WelcomePage.displayName = "WelcomePage";

export default WelcomePage;
