import { MdBrokenImage, MdHelpOutline } from "react-icons/md";
import { FiSettings, FiGlobe, FiHardDrive, FiSearch } from "react-icons/fi";
import React from "react";
import { useTranslation } from "react-i18next";

import toast from "../../../../bakaui/components/Toast";

import { Button, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";

type Props = {
  id: number;
  afterClearingCache?: () => any;
};

const priorities = [
  { icon: FiSettings, key: "manual" },
  { icon: FiGlobe, key: "external" },
  { icon: FiHardDrive, key: "cache" },
  { icon: FiSearch, key: "discovery" },
] as const;

const FallbackCover = ({ id, afterClearingCache }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const showModal = () => {
    createPortal(Modal, {
      defaultVisible: true,
      size: "lg",
      title: t<string>("ResourceCover.CoverTips.Title"),
      children: (
        <div className="flex flex-col gap-5">
          <p className="text-sm text-default-500">
            {t<string>("ResourceCover.CoverTips.description")}
          </p>

          <div className="flex flex-col gap-3">
            {priorities.map(({ icon: Icon, key }, index) => (
              <div key={key} className="flex items-start gap-3">
                <div className="flex items-center justify-center w-7 h-7 rounded-full bg-default-100 text-default-600 shrink-0 mt-0.5">
                  <span className="text-xs font-medium">{index + 1}</span>
                </div>
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    <Icon className="text-default-500" size={14} />
                    <span className="font-medium text-sm">
                      {t<string>(`ResourceCover.CoverTips.priority.${key}`)}
                    </span>
                  </div>
                  <p className="text-xs text-default-400 mt-0.5">
                    {t<string>(`ResourceCover.CoverTips.priority.${key}.desc`)}
                  </p>
                </div>
              </div>
            ))}
          </div>

          <div>
            <p className="text-sm text-default-500 mb-2">
              {t<string>("ResourceCover.CoverTips.noCover")}
            </p>
            <ul className="list-disc pl-5 text-sm text-default-600 space-y-1">
              <li>{t<string>("ResourceCover.CoverTips.action.manualSet")}</li>
              <li>{t<string>("ResourceCover.CoverTips.action.enhancer")}</li>
              <li>{t<string>("ResourceCover.CoverTips.action.ffmpeg")}</li>
            </ul>
          </div>

          <div>
            <Button
              color={"secondary"}
              size={"sm"}
              variant={"flat"}
              onPress={async () => {
                try {
                  const rsp = await BApi.cache.refreshResourceCache(id);
                  if (!rsp.code) {
                    afterClearingCache?.();
                    toast.success(t<string>("resource.action.refreshCache.success"));
                  }
                } catch (err) {
                  toast.danger("Failed");
                }
              }}
            >
              {t<string>("resource.action.refreshCache")}
            </Button>
          </div>
        </div>
      ),
      footer: { actions: ["ok"] },
    });
  };

  return (
    <Tooltip
      content={
        <Button
          onPress={showModal}
          size={"sm"}
          variant={"flat"}
          color={"primary"}
          startContent={<MdHelpOutline className={"text-lg"} />}
        >
          {t<string>("ResourceCover.CoverTips.Tooltip")}
        </Button>
      }
      delay={2000}
    >
      <div className="inline-flex">
        <MdBrokenImage className={"text-2xl opacity-50"} />
      </div>
    </Tooltip>
  );
};

export default FallbackCover;
