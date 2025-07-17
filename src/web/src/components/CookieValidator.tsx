"use client";

import type { CookieValidatorTarget } from "@/sdk/constants";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { CloseCircleOutlined } from "@ant-design/icons";
import { CheckCircleOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { Button, Textarea } from "@/components/bakaui";

export default ({
  cookie,
  target,
  onChange = (v) => {},
}: {
  cookie: string | undefined;
  target: CookieValidatorTarget;
  onChange: any;
}) => {
  const { t } = useTranslation();
  const [status, setStatus] = useState<"loading" | "failed" | "succeed">();

  const renderStatus = () => {
    if (!status) {
      return null;
    }
    switch (status) {
      case "loading":
        return null;
      case "failed":
        return <CloseCircleOutlined className={"text-base text-danger"} />;
      case "succeed":
        return <CheckCircleOutlined className={"text-base text-success"} />;
    }
  };

  return (
    <div className={"cookie-validator"}>
      <div>
        <Textarea
          label={"Cookie"}
          size={"sm"}
          value={cookie}
          onValueChange={(v) => {
            onChange(v);
          }}
        />
      </div>
      <div className={"flex items-center gap-2 mt-1"}>
        <Button
          color={"primary"}
          isDisabled={!cookie || !cookie.length}
          isLoading={status === "loading"}
          size={"sm"}
          variant={"flat"}
          onClick={() => {
            if (cookie && cookie.length > 0) {
              setStatus("loading");
              BApi.tool
                .validateCookie({
                  target,
                  cookie,
                })
                .then((a) => {
                  if (!a.code) {
                    setStatus("succeed");
                  } else {
                    setStatus("failed");
                  }
                })
                .catch(() => {
                  setStatus("failed");
                });
            }
          }}
        >
          {t<string>("Validate")}
        </Button>
        {renderStatus()}
      </div>
    </div>
  );
};
