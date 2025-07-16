"use client";

import { Trans, useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";

import {
  Accordion,
  AccordionItem,
  Link,
  Modal,
  Snippet,
  Spacer,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { createPortalOfComponent } from "@/components/utils";

interface IProps {}

const ErrorModal = ({}: IProps) => {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [appInfo, setAppInfo] = useState<{ logPath: string }>();

  useEffect(() => {
    BApi.app.getAppInfo().then((rsp) => {
      if (!rsp.code) {
        setAppInfo({
          logPath: rsp.data!.logPath!,
        });
      }
    });
  }, []);

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"lg"}
      title={t<string>(
        "We have encountered some problems. You could try the following steps:",
      )}
    >
      <Accordion isCompact selectedKeys={"all"} selectionMode={"multiple"}>
        <AccordionItem
          key="1"
          title={
            <span className={"font-bold"}>{t<string>("Simply retry")}</span>
          }
        >
          {t<string>("Press 'F5' to reload the page.")}
        </AccordionItem>
        <AccordionItem
          key="2"
          title={
            <span className={"font-bold"}>{t<string>("Restart the app")}</span>
          }
        >
          {t<string>("Shutdown and restart the app completely.")}
        </AccordionItem>
        <AccordionItem
          key="3"
          title={
            <span className={"font-bold"}>{t<string>("Contact support")}</span>
          }
        >
          <div className={"flex flex-col gap-1 mb-2"}>
            <div>
              {t<string>("You can find the latest log file at")}
              <Spacer y={1} />
              <Snippet
                hideSymbol
                className={"cursor-pointer"}
                size={"sm"}
                style={{ color: "var(--bakaui-primary)" }}
                onClick={() => {
                  if (appInfo?.logPath) {
                    BApi.tool.openFileOrDirectory({ path: appInfo.logPath });
                  }
                }}
              >
                <span className={"break-all whitespace-break-spaces"}>
                  {appInfo?.logPath}
                </span>
              </Snippet>
              <Spacer y={1} />
            </div>
            <div className={""}>
              <span className={"font-bold"}>
                {t<string>("If you have no programming experience,")}
              </span>
              &nbsp;
              {t<string>(
                "please provide the latest log file to the support team.",
              )}
            </div>
            <div className={""}>
              <span className={"font-bold"}>{t<string>("Otherwise,")}</span>
              &nbsp;
              {t<string>(
                "you can locate and collect the error messages in the log file, and provide they to the support team. (open an issue on github, or send to developer directly)",
              )}
            </div>
            <div className={"mt-2"}>
              <Trans
                i18nKey={"ErrorHandlingModal.FindContactInConfigurationPage"}
              >
                You can find the concat at the bottom of
                <Link
                  className={"cursor-pointer"}
                  size={"sm"}
                  onClick={() => {
                    navigate("/configuration");
                  }}
                >
                  {t<string>("Configuration page")}
                </Link>
              </Trans>
            </div>
          </div>
        </AccordionItem>
        <AccordionItem
          key="4"
          title={
            <span className={"font-bold"}>
              {t<string>("Try other features")}
            </span>
          }
        >
          <Link
            className={"cursor-pointer"}
            size={"sm"}
            onClick={() => {
              navigate("/");
            }}
          >
            {t<string>("Return to homepage")}
          </Link>
        </AccordionItem>
      </Accordion>
    </Modal>
  );
};

export default ErrorModal;
