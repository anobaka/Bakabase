"use client";

import React, { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";

import { Button, Chip, Popover } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export default () => {
  const [version, setVersion] = useState<string>();
  const { t } = useTranslation();
  const navigate = useNavigate();

  useEffect(() => {
    BApi.app.getAppInfo().then((a) => {
      setVersion(a.data?.coreVersion ?? "0.0.0");
    });
  }, []);

  return (
    <div className={"flex items-center justify-center w-screen h-screen"}>
      <div className={"flex flex-col gap-4"}>
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
        <div className={"flex items-center gap-1"}>
          {t<string>(
            "Please read terms and conditions carefully before you start to use this app",
          )}
          <Popover
            trigger={
              <Button
                // size={'sm'}
                color={"success"}
                radius={"sm"}
                variant={"light"}
              >
                {t<string>("Click to check")}
              </Button>
            }
          >
            {t<string>(
              "We are using Microsoft Clarity to track anonymous data, which will help us to improve our product experience.",
            )}
          </Popover>
        </div>
        <div className={"flex justify-center"}>
          <Button
            color={"primary"}
            onPress={() => {
              BApi.app.acceptTerms().then((a) => {
                navigate("/");
              });
            }}
          >
            {t<string>("Accept and start to use")}
          </Button>
        </div>
      </div>
    </div>
  );
};
