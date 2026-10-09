import { useState } from "react";
import { useTranslation } from "react-i18next";
import { CheckOutlined, CopyOutlined } from "@ant-design/icons";
import { Button } from "@heroui/react";

import { copyTextToClipboard } from "@/core/clipboard";

/** Full error text stays selectable, scrollable and copyable, including on plain HTTP LAN pages. */
export default function ErrorDetails({
  text,
  className = "",
}: {
  text: string;
  className?: string;
}) {
  const { t } = useTranslation();
  const [copiedText, setCopiedText] = useState<string>();
  const [copyFailed, setCopyFailed] = useState(false);
  const copied = copiedText === text;

  const copy = async () => {
    try {
      await copyTextToClipboard(text);
      setCopiedText(text);
      setCopyFailed(false);
    } catch {
      setCopiedText(undefined);
      setCopyFailed(true);
    }
  };

  return (
    <div className={`min-w-0 overflow-hidden rounded-xl border border-default-200 ${className}`}>
      <div className="flex items-center justify-between gap-3 border-b border-default-200 bg-default-100/60 px-3 py-2">
        <span className="text-xs font-medium text-foreground-500">{t("error.details.title")}</span>
        <Button
          size="sm"
          startContent={copied ? <CheckOutlined aria-hidden /> : <CopyOutlined aria-hidden />}
          variant="light"
          onPress={copy}
        >
          {t(copied ? "error.details.copied" : "error.details.copy")}
        </Button>
      </div>
      <div
        aria-label={t<string>("error.details.title")}
        className="max-h-[min(50dvh,24rem)] touch-pan-y overflow-auto overscroll-contain p-3 outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary"
        role="region"
        // Keyboard users need to focus the long error to scroll it with arrow/Page Down keys.
        // eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex
        tabIndex={0}
      >
        <pre className="m-0 select-text whitespace-pre-wrap break-words font-mono text-xs leading-relaxed [overflow-wrap:anywhere]">
          {text}
        </pre>
      </div>
      {copyFailed && (
        <p className="px-3 pb-3 text-xs text-danger" role="status">
          {t("error.details.copyFailed")}
        </p>
      )}
    </div>
  );
}
