"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { GithubOutlined } from "@ant-design/icons";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Link,
  Modal,
  Snippet,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import Urls from "@/cons/Urls";

interface IProps {
  error?: Error;
  errorInfo?: React.ErrorInfo;
}

interface StepProps {
  number: number;
  title: string;
  children: React.ReactNode;
}

const Step = ({ number, title, children }: StepProps) => (
  <div className="flex items-start gap-2 py-1.5">
    <div className="flex-shrink-0 w-5 h-5 rounded-full bg-primary flex items-center justify-center text-white text-xs font-bold">
      {number}
    </div>
    <div className="flex-1 min-w-0">
      <span className="font-semibold text-sm">{title}</span>
      <span className="text-xs text-default-500 ml-2">{children}</span>
    </div>
  </div>
);

const ErrorModal = ({ error, errorInfo }: IProps) => {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const [appInfo, setAppInfo] = useState<{ logPath: string }>();
  const [showFullStack, setShowFullStack] = useState(false);
  const [showFullComponentStack, setShowFullComponentStack] = useState(false);

  useEffect(() => {
    BApi.app.getAppInfo().then((rsp) => {
      if (!rsp.code) {
        setAppInfo({
          logPath: rsp.data!.logPath!,
        });
      }
    });
  }, []);

  const truncateStack = (stack: string, maxLines: number = 5) => {
    const lines = stack.split("\n");
    if (lines.length <= maxLines) return { text: stack, truncated: false };
    return {
      text: lines.slice(0, maxLines).join("\n") + "\n...",
      truncated: true,
      totalLines: lines.length,
    };
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel"],
      }}
      size={"xl"}
      title={
        <div className="flex items-center gap-2">
          <span className="text-2xl">!</span>
          <span>{t<string>("Something went wrong")}</span>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {error && (
          <div className="border border-danger-200 rounded-lg overflow-hidden">
            <div className="bg-danger-50 px-3 py-1.5 border-b border-danger-200 flex items-center justify-between">
              <span className="font-semibold text-danger-600 text-sm">
                {error.name || "Error"}
              </span>
              <Chip color="danger" size="sm" variant="flat">
                {t<string>("Error")}
              </Chip>
            </div>
            <div className="p-3 flex flex-col gap-2">
              <div className="text-sm text-danger-600 bg-danger-50 p-2 rounded border border-danger-100">
                {error.message || t<string>("Unknown error")}
              </div>

              {error.stack && (
                <Accordion isCompact selectionMode="multiple">
                  <AccordionItem
                    key="stack"
                    title={<span className="text-xs text-default-500">{t<string>("Stack Trace")}</span>}
                  >
                    <Snippet hideSymbol className="w-full" radius="sm" size="sm" color="default">
                      <pre
                        className="text-xs whitespace-pre-wrap break-all font-mono"
                        style={{ maxHeight: showFullStack ? "none" : "6rem", overflowY: "auto" }}
                      >
                        {showFullStack ? error.stack : truncateStack(error.stack).text}
                      </pre>
                    </Snippet>
                    {truncateStack(error.stack).truncated && (
                      <Button size="sm" variant="light" onClick={() => setShowFullStack(!showFullStack)}>
                        {showFullStack ? t<string>("Show less") : t<string>("Show all")}
                      </Button>
                    )}
                  </AccordionItem>
                </Accordion>
              )}
              {errorInfo?.componentStack && (
                <Accordion isCompact selectionMode="multiple">
                  <AccordionItem
                    key="component-stack"
                    title={<span className="text-xs text-default-500">{t<string>("Component Stack")}</span>}
                  >
                    <Snippet hideSymbol className="w-full" radius="sm" size="sm" color="default">
                      <pre
                        className="text-xs whitespace-pre-wrap break-all font-mono"
                        style={{ maxHeight: showFullComponentStack ? "none" : "6rem", overflowY: "auto" }}
                      >
                        {showFullComponentStack ? errorInfo.componentStack : truncateStack(errorInfo.componentStack).text}
                      </pre>
                    </Snippet>
                    {truncateStack(errorInfo.componentStack).truncated && (
                      <Button size="sm" variant="light" onClick={() => setShowFullComponentStack(!showFullComponentStack)}>
                        {showFullComponentStack ? t<string>("Show less") : t<string>("Show all")}
                      </Button>
                    )}
                  </AccordionItem>
                </Accordion>
              )}
            </div>
          </div>
        )}

        <div className="border border-default-200 rounded-lg overflow-hidden">
          <div className="bg-default-100 px-3 py-1.5 border-b border-default-200">
            <span className="font-semibold text-sm">{t<string>("What you can try")}</span>
          </div>
          <div className="p-2 flex flex-col gap-1">
            <Step number={1} title={t<string>("Reload the page")}>
              <kbd className="px-1 py-0.5 text-xs bg-default-200 rounded font-mono">F5</kbd>
            </Step>
            <Step number={2} title={t<string>("Restart the app")}>
              {t<string>("Shutdown and restart completely")}
            </Step>
            <Step number={3} title={t<string>("Contact support")}>
              <div className="flex flex gap-1.5 mt-1">
                {appInfo?.logPath && (
                  <div className="text-xs text-default-500">
                    {t<string>("Log file:")}
                    <Snippet
                      hideSymbol
                      className="cursor-pointer ml-1"
                      size="sm"
                      onClick={() => BApi.tool.openFileOrDirectory({ path: appInfo.logPath })}
                    >
                      <span className="break-all whitespace-break-spaces text-primary">{appInfo.logPath}</span>
                    </Snippet>
                  </div>
                )}
                <div className="flex items-center gap-2">
                  <Button
                    color="default"
                    size="sm"
                    onClick={() => BApi.gui.openUrlInDefaultBrowser({ url: Urls.Github })}
                  >
                    <GithubOutlined />
                    GitHub
                  </Button>
                </div>
              </div>
            </Step>
            <Step number={4} title={t<string>("Continue browsing")}>
              <Link className="cursor-pointer" size="sm" onClick={() => navigate("/")}>
                {t<string>("Return to homepage")}
              </Link>
            </Step>
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default ErrorModal;
