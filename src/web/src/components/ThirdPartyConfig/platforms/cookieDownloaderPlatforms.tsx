"use client";

import type { FC } from "react";

import BApi from "@/sdk/BApi";
import { CookieValidatorTarget, ThirdPartyId } from "@/sdk/constants";
import {
  useBilibiliOptionsStore,
  usePixivOptionsStore,
  useCienOptionsStore,
  useFanboxOptionsStore,
  useFantiaOptionsStore,
  usePatreonOptionsStore,
} from "@/stores/options";

import CookieDownloaderConfigPanel, {
  CookieDownloaderConfigField,
  type CookieDownloaderConfigPanelProps,
} from "../base/CookieDownloaderConfigPanel";
import ThirdPartyConfigModal from "../base/ThirdPartyConfigModal";

type PanelProps = {
  fields?: CookieDownloaderConfigField[] | "all";
};

type ModalExtra = PanelProps & {
  isOpen?: boolean;
  onClose?: () => void;
  onDestroyed?: () => void;
};

function wrapModal(title: string, Inner: FC<PanelProps>) {
  const ModalCmp: FC<ModalExtra> = ({ isOpen, onClose, onDestroyed, fields }) => {
    const handleClose = onClose ?? onDestroyed;
    return (
      <ThirdPartyConfigModal title={title} isOpen={isOpen} onClose={handleClose}>
        <Inner fields={fields} />
      </ThirdPartyConfigModal>
    );
  };
  return ModalCmp;
}

function makePlatform(
  title: string,
  thirdPartyId: ThirdPartyId,
  useStore: typeof useBilibiliOptionsStore,
  patchApi: CookieDownloaderConfigPanelProps["patchApi"],
  cookieValidatorTarget: CookieValidatorTarget,
  cookieCaptureTarget: CookieValidatorTarget,
) {
  const Panel: FC<PanelProps> = ({ fields = "all" }) => {
    const options = useStore((s: any) => s.data);
    const patch = useStore((s: any) => s.patch);
    return (
      <CookieDownloaderConfigPanel
        title={title}
        thirdPartyId={thirdPartyId}
        fields={fields}
        options={options}
        patch={patch}
        patchApi={patchApi}
        cookieValidatorTarget={cookieValidatorTarget}
        cookieCaptureTarget={cookieCaptureTarget}
      />
    );
  };
  const Modal = wrapModal(title, Panel);
  return { Panel, Modal };
}

const bilibili = makePlatform(
  "Bilibili",
  ThirdPartyId.Bilibili,
  useBilibiliOptionsStore,
  BApi.options.patchBilibiliOptions,
  CookieValidatorTarget.BiliBili,
  CookieValidatorTarget.BiliBili,
);
export const BilibiliConfigPanel = bilibili.Panel;
export const BilibiliConfigModal = bilibili.Modal;
export const BilibiliConfig = bilibili.Modal;

const pixiv = makePlatform(
  "Pixiv",
  ThirdPartyId.Pixiv,
  usePixivOptionsStore,
  BApi.options.patchPixivOptions,
  CookieValidatorTarget.Pixiv,
  CookieValidatorTarget.Pixiv,
);
export const PixivConfigPanel = pixiv.Panel;
export const PixivConfigModal = pixiv.Modal;
export const PixivConfig = pixiv.Modal;

const cien = makePlatform(
  "Cien",
  ThirdPartyId.Cien,
  useCienOptionsStore,
  BApi.options.patchCienOptions,
  CookieValidatorTarget.Cien,
  CookieValidatorTarget.Cien,
);
export const CienConfigPanel = cien.Panel;
export const CienConfigModal = cien.Modal;
export const CienConfig = cien.Modal;

const fanbox = makePlatform(
  "Fanbox",
  ThirdPartyId.Fanbox,
  useFanboxOptionsStore,
  BApi.options.patchFanboxOptions,
  CookieValidatorTarget.Fanbox,
  CookieValidatorTarget.Fanbox,
);
export const FanboxConfigPanel = fanbox.Panel;
export const FanboxConfigModal = fanbox.Modal;
export const FanboxConfig = fanbox.Modal;

const fantia = makePlatform(
  "Fantia",
  ThirdPartyId.Fantia,
  useFantiaOptionsStore,
  BApi.options.patchFantiaOptions,
  CookieValidatorTarget.Fantia,
  CookieValidatorTarget.Fantia,
);
export const FantiaConfigPanel = fantia.Panel;
export const FantiaConfigModal = fantia.Modal;
export const FantiaConfig = fantia.Modal;

const patreon = makePlatform(
  "Patreon",
  ThirdPartyId.Patreon,
  usePatreonOptionsStore,
  BApi.options.patchPatreonOptions,
  CookieValidatorTarget.Patreon,
  CookieValidatorTarget.Patreon,
);
export const PatreonConfigPanel = patreon.Panel;
export const PatreonConfigModal = patreon.Modal;
export const PatreonConfig = patreon.Modal;

export { CookieDownloaderConfigField };
