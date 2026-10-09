import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCloudServer, AiOutlineDesktop, AiOutlineDown } from "react-icons/ai";

import { localToolAppUrl, localToolRoute, localToolServerUrl, type LocalTool } from "./localTools";

import { Button, Input, Popover } from "@/components/bakaui";
import { openLocalView } from "@/features/federation/switching";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

/** File tools act on the shown service even when a desktop relay handles play/open locally. */
export default function ToolExecutionNotice({ tool }: { tool: LocalTool }) {
  const { t } = useTranslation();
  const context = useRemoteAccessStore((state) => state.context);
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const clientHost = useRemoteAccessStore((state) => state.clientHost);
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const [switching, setSwitching] = useState(false);
  const [switchFailed, setSwitchFailed] = useState(false);
  const [attempted, setAttempted] = useState(false);
  const [address, setAddress] = useState("");
  const [invalidAddress, setInvalidAddress] = useState(false);

  // A loopback connection can come from Docker or a reverse proxy. Keep the wording
  // about the service whose files are shown, without claiming where this browser runs.

  const canSwitch =
    context === "known" && clientMode === ClientMode.PureClient && clientHost === "console";
  const offerLocal = context !== "known" || clientMode !== ClientMode.AllInOne;
  const serverUrl = localToolServerUrl(address, tool);

  return (
    <Popover
      placement="bottom-end"
      trigger={
        <Button
          className="h-7 min-w-0 shrink-0 gap-1.5 px-2 text-xs text-default-600"
          size="sm"
          variant="light"
        >
          <AiOutlineCloudServer aria-hidden className="text-base" />
          {t(
            context === "known" && clientMode !== ClientMode.AllInOne
              ? "toolExecution.serverFiles"
              : "toolExecution.currentService",
          )}
          <AiOutlineDown aria-hidden className="text-[10px]" />
        </Button>
      }
    >
      <div className="w-80 max-w-[calc(100vw-48px)] space-y-3 p-2 text-sm">
        <div>
          <h2 className="font-medium text-foreground">{t("toolExecution.title")}</h2>
          {context === "known" && serverName && (
            <p className="mt-1 break-words font-medium text-default-600">{serverName}</p>
          )}
          <p className="mt-1 leading-relaxed text-default-500">{t("toolExecution.description")}</p>
        </div>
        {offerLocal && (
          <div className="border-t border-default-200 pt-3">
            <p className="mb-2 leading-relaxed text-default-600">{t("toolExecution.useHere")}</p>
            {canSwitch ? (
              <Button
                color="primary"
                isLoading={switching}
                size="sm"
                startContent={<AiOutlineDesktop aria-hidden className="text-base" />}
                onPress={async () => {
                  if (switching) return;
                  setSwitching(true);
                  setSwitchFailed(false);
                  try {
                    await openLocalView(localToolRoute(tool));
                  } catch {
                    setSwitchFailed(true);
                  } finally {
                    setSwitching(false);
                  }
                }}
              >
                {t("toolExecution.switchLocal")}
              </Button>
            ) : (
              <a
                className="inline-flex h-8 items-center gap-2 rounded-lg bg-primary px-3 text-xs font-medium text-primary-foreground outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                href={localToolAppUrl(tool)}
                rel="noreferrer"
                onClick={() => setAttempted(true)}
              >
                <AiOutlineDesktop aria-hidden className="text-base" />
                {t("toolExecution.openApp")}
              </a>
            )}
            {switchFailed && (
              <p className="mt-2 text-danger" role="alert">
                {t("toolExecution.switchFailed")}
              </p>
            )}
            {canSwitch && (
              <p className="mt-2 text-xs leading-relaxed text-default-500">
                {t("toolExecution.deviceMenu")}
              </p>
            )}
            {attempted && (
              <p className="mt-2 text-xs leading-relaxed text-default-500" role="status">
                {t("toolExecution.appFallback")}
              </p>
            )}
          </div>
        )}
        {offerLocal && !canSwitch && (
          <details className="border-t border-default-200 pt-2">
            <summary className="cursor-pointer text-xs text-default-600">
              {t("toolExecution.localServer")}
            </summary>
            <p className="my-2 text-xs leading-relaxed text-default-500">
              {t("toolExecution.localServerDescription")}
            </p>
            <form
              className="space-y-2"
              onSubmit={(event) => {
                event.preventDefault();
                if (!serverUrl) {
                  setInvalidAddress(true);

                  return;
                }
                window.open(serverUrl, "_blank", "noopener,noreferrer");
              }}
            >
              <Input
                errorMessage={invalidAddress ? t("toolExecution.invalidAddress") : undefined}
                isInvalid={invalidAddress}
                label={t("toolExecution.address")}
                placeholder="http://127.0.0.1:34567"
                size="sm"
                value={address}
                onValueChange={(value) => {
                  setAddress(value);
                  setInvalidAddress(false);
                }}
              />
              <Button size="sm" type="submit" variant="flat">
                {t("toolExecution.openServer")}
              </Button>
            </form>
          </details>
        )}
      </div>
    </Popover>
  );
}
