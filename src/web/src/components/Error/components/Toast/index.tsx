"use client";

import type { Toast } from "react-hot-toast";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import { CheckOutlined, CloseOutlined, CopyOutlined, WarningOutlined } from "@ant-design/icons";
import toast from "react-hot-toast";

import { Button, Chip } from "@/components/bakaui";

type Props = {
  toast: Toast;
  title: string;
  description?: string;
};
const Toast = (props: Props) => {
  const { toast: tst, title, description } = props;
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);

  return (
    <div className={"flex items-start gap-4 max-w-full"}>
      <Chip classNames={{ content: "flex" }} className={"shrink-0"} color={"danger"} variant={"light"}>
        <WarningOutlined className={"text-xl"} />
      </Chip>
      <div className={"flex flex-col gap-1 min-w-0 flex-1 break-words"}>
        <div>{title}</div>
        {description && (
          <pre className={"opacity-80 text-xs whitespace-pre-wrap break-words max-h-60 overflow-auto"}>
            {description}
          </pre>
        )}
      </div>
      <div className={"flex items-center shrink-0"}>
        <Button
          isIconOnly
          size={"sm"}
          variant={"light"}
          onClick={async () => {
            try {
              let text = title;

              if (description) {
                text += `\n${description}`;
              }
              await navigator.clipboard.writeText(text);
              setCopied(true);
              console.log("Text copied to clipboard");
            } catch (err) {
              console.error("Failed to copy text: ", err);
              setCopied(false);
            }
          }}
        >
          {copied ? (
            <CheckOutlined className={"text-base"} />
          ) : (
            <CopyOutlined className={"text-base"} />
          )}
        </Button>
        <Button isIconOnly size={"sm"} variant={"light"} onClick={() => toast.dismiss(tst.id)}>
          <CloseOutlined className={"text-base"} />
        </Button>
      </div>
    </div>
  );
};

Toast.displayName = "Toast";

export default Toast;
