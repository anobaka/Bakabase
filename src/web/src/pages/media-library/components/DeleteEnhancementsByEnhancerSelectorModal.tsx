"use client";

import type { MediaLibraryTemplatePage } from "@/pages/media-library-template/models";

import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Modal, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DeleteEnhancementsModal from "@/pages/category/components/DeleteEnhancementsModal";
import BApi from "@/sdk/BApi";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";

type Props = {
  mediaLibraryId: number;
  mediaLibraryName: string;
  template: MediaLibraryTemplatePage | undefined;
  onCompleted?: () => void;
  onDestroyed?: () => void;
};

const DeleteEnhancementsByEnhancerSelectorModal = ({
  mediaLibraryId,
  mediaLibraryName,
  template,
  onCompleted,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [loading, setLoading] = useState(true);
  const [enhancerMap, setEnhancerMap] = useState<Record<number, { id: number; name: string }>>({});
  const [countMap, setCountMap] = useState<Record<number, number>>({});

  const enhancerIdsFromTemplate = useMemo(() => {
    return (template?.enhancers ?? []).map((e) => e.enhancerId);
  }, [template]);

  useEffect(() => {
    let mounted = true;
    const load = async () => {
      try {
        const er = await BApi.enhancer.getAllEnhancerDescriptors();
        const arr = er.data ?? [];
        const map: Record<number, { id: number; name: string }> = {};
        arr.forEach((d: any) => {
          map[d.id] = { id: d.id, name: d.name };
        });
        if (mounted) setEnhancerMap(map);
      } finally {
        if (mounted) setLoading(false);
      }
    };
    load();

    const loadCounts = async () => {
      if (!mediaLibraryId || enhancerIdsFromTemplate.length === 0) return;
      try {
        const r = await BApi.mediaLibrary.getEnhancedResourceCountsByMediaLibrary(
          mediaLibraryId,
          { enhancerIds: enhancerIdsFromTemplate },
        );
      } catch {
        setCountMap({});
      }
    };
    loadCounts();

    return () => {
      mounted = false;
    };
  }, []);

  const handleClickEnhancer = (enhancerId: number) => {
    const enhancerName = enhancerMap[enhancerId]?.name ?? `#${enhancerId}`;
    createPortal(DeleteEnhancementsModal, {
      title: t<string>(
        "Delete all enhancement records of this enhancer for category {{categoryName}}",
        { categoryName: mediaLibraryName },
      ),
      onOk: async (deleteEmptyOnly: boolean) => {
        await BApi.mediaLibrary.deleteEnhancementsByMediaLibraryAndEnhancer(
          mediaLibraryId,
          enhancerId,
          { deleteEmptyOnly },
        );
        onCompleted?.();
      },
    });
  };

  return (
    <Modal
      defaultVisible
      title={t<string>("Enhancement.DeleteByEnhancer.SelectEnhancerTitle")}
      onDestroyed={onDestroyed}
      footer={{ actions: ["cancel"] }}
    >
      {loading ? (
        <div className="flex items-center justify-center py-4">
          <Spinner />
        </div>
      ) : enhancerIdsFromTemplate.length === 0 ? (
        <div className="text-sm text-gray-500">
          {t<string>("Enhancement.DeleteByEnhancer.NoEnhancerInTemplate")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {enhancerIdsFromTemplate.map((id) => (
            <Button key={id} onPress={() => handleClickEnhancer(id)}>
              <div className="flex items-center justify-between w-full">
                <BriefEnhancer enhancer={enhancerMap[id]} />
                <Chip size={"sm"} variant={"flat"}>
                  {t<string>("Enhancement.EnhancedResourceCount", { count: countMap[id] ?? 0 })}
                </Chip>
              </div>
            </Button>
          ))}
        </div>
      )}
    </Modal>
  );
};

DeleteEnhancementsByEnhancerSelectorModal.displayName =
  "DeleteEnhancementsByEnhancerSelectorModal";

export default DeleteEnhancementsByEnhancerSelectorModal;


