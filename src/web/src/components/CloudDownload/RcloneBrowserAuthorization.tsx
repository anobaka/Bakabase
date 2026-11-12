import { useEffect, useRef, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Button, Input, Spinner } from "@/components/bakaui";
import { openExternalUrl } from "@/utils/openExternalUrl";
import type { BakabaseServiceComponentsCloudDownloadsRcloneRcloneAuthorizationSessionViewModel as Session } from "@/sdk/Api";

/** The panel owns the session lifecycle; this view never stores a callback outside its input. */
export default function RcloneBrowserAuthorization({
  session,
  busy,
  problemLabel,
  onStart,
  onStop,
  onSubmit,
  children,
}: {
  session: Session;
  busy: boolean;
  problemLabel: (message: string) => string;
  onStart: () => Promise<boolean>;
  onStop: () => Promise<boolean>;
  onSubmit: (callbackUrl: string) => Promise<boolean>;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  const [advanced, setAdvanced] = useState(false);
  const [callbackUrl, setCallbackUrl] = useState("");
  const mounted = useRef(false);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const browser = session.browser;
  const automatic = session.authorizationMode === "automatic";
  const exchanging = browser?.status === "exchanging";
  const canContinue =
    browser?.status === "awaitingCallback" &&
    !!browser.authorizationUrl &&
    new Date(browser.expiresAt).getTime() > Date.now();

  return (
    <div className="space-y-3">
      {!advanced && (
        <div className="space-y-3">
          <p className="text-sm text-default-600">
            {t(automatic ? "rclone.browser.automaticIntroduction" : "rclone.browser.introduction")}
          </p>
          {busy && !browser && <Spinner size="sm" />}
          {exchanging && (
            <p role="status" className="text-sm text-default-600">
              {t("rclone.browser.exchanging")}
            </p>
          )}
          {browser?.problem && (
            <p role="alert" className="text-sm text-warning-600">
              {problemLabel(browser.problem)}
            </p>
          )}
          <div className="space-y-2">
            <p className="text-sm font-medium">{t("rclone.browser.openStep")}</p>
            <Button
              size="sm"
              color="primary"
              isDisabled={busy || !canContinue}
              onPress={() => {
                // Preserve browser user activation: no request or await before opening.
                if (canContinue) openExternalUrl(browser!.authorizationUrl!);
              }}
            >
              {t("rclone.browser.openLogin")}
            </Button>
          </div>
          {automatic ? (
            canContinue && (
              <p role="status" className="text-sm text-default-600">
                {t("rclone.browser.automaticWaiting")}
              </p>
            )
          ) : (
            <div className="space-y-2">
              <p className="text-sm font-medium">{t("rclone.browser.callbackStep")}</p>
              <p className="text-xs text-default-500">{t("rclone.browser.callbackHint")}</p>
              <Input
                label={t("rclone.browser.callbackUrl")}
                type="password"
                autoComplete="off"
                spellCheck="false"
                isRequired
                isDisabled={busy || !canContinue}
                value={callbackUrl}
                onValueChange={setCallbackUrl}
              />
              <Button
                size="sm"
                color="primary"
                isDisabled={busy || !canContinue || !callbackUrl.trim()}
                isLoading={busy}
                onPress={() => {
                  const value = callbackUrl.trim();
                  setCallbackUrl("");
                  void onSubmit(value);
                }}
              >
                {t("rclone.browser.submit")}
              </Button>
            </div>
          )}
          {!canContinue && !busy && !exchanging && (
            <Button
              size="sm"
              variant="flat"
              onPress={() => {
                setCallbackUrl("");
                void onStart();
              }}
            >
              {t("rclone.browser.restart")}
            </Button>
          )}
        </div>
      )}
      {!automatic && (
        <Accordion
          isCompact
          selectedKeys={advanced ? ["cli"] : []}
          isDisabled={busy}
          onSelectionChange={(keys) => {
            const next = keys === "all" || keys.has("cli");
            if (busy || next === advanced) return;
            setCallbackUrl("");
            void (async () => {
              // Stop the server listener before asking the user to start the CLI alternative.
              if (next && !(await onStop())) return;
              if (!mounted.current) return;
              setAdvanced(next);
              if (!next) await onStart();
            })();
          }}
        >
          <AccordionItem key="cli" title={t("rclone.browser.advanced")}>
            <div className="space-y-3">{children}</div>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
}
