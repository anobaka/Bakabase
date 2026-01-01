import { MdBrokenImage, MdHelpOutline } from "react-icons/md";
import React from "react";
import { useTranslation } from "react-i18next";

import toast from "../../../../bakaui/components/Toast";

import { Button, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import { ResourceCacheType } from "@/sdk/constants.ts";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";

type Props = {
  id: number;
  afterClearingCache?: () => any;
};

const FallbackCover = ({ id, afterClearingCache }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const showModal = async () => {
    const cacheExists =
      (await BApi.cache.checkResourceCacheExistence(id, ResourceCacheType.Covers)).data ??
      false;

    createPortal(Modal, {
      defaultVisible: true,
      size: "lg",
      title: t<string>("ResourceCover.CoverTips.Title"),
      children: (
        <div className="flex flex-col gap-6">
          <div>
            <div className="font-medium">{t<string>("ResourceCover.CoverTips.S1.Title")}</div>
            <div className="text-sm mb-1">
              {t<string>("ResourceCover.CoverTips.S1.Happens")}
            </div>
            <ul className="list-disc pl-5 text-sm">
              <li>{t<string>("ResourceCover.CoverTips.S1.When.NotImage")}</li>
              <li>{t<string>("ResourceCover.CoverTips.S1.When.NoImageInFolder")}</li>
            </ul>
            <div className="text-sm mt-1">{t<string>("ResourceCover.CoverTips.S1.Todo")}</div>
            <ol className="list-decimal pl-5 text-sm">
              <li>{t<string>("ResourceCover.CoverTips.S1.Todo.ManualSet")}</li>
              <li>{t<string>("ResourceCover.CoverTips.S1.Todo.FFmpeg")}</li>
              <li>{t<string>("ResourceCover.CoverTips.S1.Todo.Enhancers")}</li>
              <li>{t<string>("ResourceCover.CoverTips.S1.Todo.Cache")}</li>
            </ol>
          </div>
          <div>
            <div className="font-medium">{t<string>("ResourceCover.CoverTips.S2.Title")}</div>
            <div className="text-sm mb-1">
              {t<string>("ResourceCover.CoverTips.S2.Happens")}
            </div>
            <ul className="list-disc pl-5 text-sm">
              <li>{t<string>("ResourceCover.CoverTips.S2.When.CacheDeleted")}</li>
            </ul>
            <div className="text-sm mt-1">{t<string>("ResourceCover.CoverTips.S2.Todo")}</div>
            <ol className="list-decimal pl-5 text-sm">
              <li>{t<string>("ResourceCover.CoverTips.S2.Todo.DisableCache")}</li>
            </ol>
          </div>
          {cacheExists && (
            <div>
              <div className="font-medium">{t<string>("Cover cache")}</div>
              <Button
                color={"secondary"}
                size={"sm"}
                variant={"flat"}
                onPress={async () => {
                  try {
                    await BApi.cache.deleteResourceCacheByResourceIdAndCacheType(
                      id,
                      ResourceCacheType.Covers,
                    );
                    await BApi.resource.discoverResourceCover(id);
                    afterClearingCache?.();
                    toast.success(t<string>("Cover cache has been reset"));
                  } catch (err) {
                    toast.danger("Failed");
                  }
                }}
              >
                {t<string>("Reset cover cache of current resource")}
              </Button>
            </div>
          )}
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
