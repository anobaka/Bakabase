import { useState } from "react";
import { useTranslation } from "react-i18next";
import { DesktopOutlined, DownloadOutlined } from "@ant-design/icons";

import { Button, Modal } from "@/components/bakaui";
import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { ClientMode } from "@/sdk/constants";
import { isDesktopAppWindow } from "@/utils/clientSurface";

/** Wait for the access context: a desktop/relay already has the required bridge. */
export const useDesktopPlaybackGuidanceAvailable = () => {
  const browserWithoutBridge = useRemoteAccessStore(
    (state) => state.context === "known" && state.clientMode === ClientMode.RemoteBrowser,
  );

  return browserWithoutBridge && !isDesktopAppWindow();
};

export const DesktopPlaybackGuide = ({ onClose }: { onClose: () => void }) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={false}
      size="md"
      title={t("resource.desktopPlayback.title")}
      onDestroyed={onClose}
    >
      {/* Portals keep React ancestors: resource menus cancel bubbled clicks and
          activation keys. Preserve native details/link defaults inside this guide. */}
      <div
        className="flex flex-col gap-4 text-sm leading-relaxed"
        role="presentation"
        onClick={(event) => event.stopPropagation()}
        onKeyDown={(event) => {
          if (event.key === "Enter" || event.key === " ") event.stopPropagation();
        }}
      >
        <p className="text-default-600">{t("resource.desktopPlayback.intro")}</p>
        <ol className="list-decimal space-y-2 pl-5">
          <li>{t("resource.desktopPlayback.install")}</li>
          <li>{t("resource.desktopPlayback.connect")}</li>
          <li>{t("resource.desktopPlayback.configure")}</li>
        </ol>
        <details className="rounded-lg border border-default-200 px-3 py-2">
          <summary className="cursor-pointer font-medium">
            {t("resource.desktopPlayback.requirements")}
          </summary>
          <ul className="mt-2 list-disc space-y-2 pl-4 text-default-500">
            <li>{t("resource.desktopPlayback.http")}</li>
            <li>{t("resource.desktopPlayback.paths")}</li>
          </ul>
        </details>
        <a
          className="inline-flex min-h-10 items-center justify-center gap-2 self-start rounded-xl bg-primary px-4 font-medium text-primary-foreground"
          href="#/other-devices"
          rel="noopener noreferrer"
          target="_blank"
        >
          <DownloadOutlined aria-hidden />
          {t("resource.desktopPlayback.download")}
        </a>
        <p className="text-xs text-default-500">{t("resource.desktopPlayback.alreadyInstalled")}</p>
      </div>
    </Modal>
  );
};

/** Opt-in guidance, never an automatic dialog or a claim that installation fixes missing files. */
export const DesktopPlaybackGuideButton = () => {
  const { t } = useTranslation();
  const available = useDesktopPlaybackGuidanceAvailable();
  const [visible, setVisible] = useState(false);

  if (!available) return null;

  return (
    <>
      <Button
        size="sm"
        startContent={<DesktopOutlined aria-hidden />}
        variant="light"
        onPress={() => setVisible(true)}
      >
        {t("resource.desktopPlayback.action")}
      </Button>
      {visible && <DesktopPlaybackGuide onClose={() => setVisible(false)} />}
    </>
  );
};
