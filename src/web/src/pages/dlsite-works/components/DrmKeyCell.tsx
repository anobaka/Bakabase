import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Tooltip } from "@heroui/react";
import {
  AiOutlineEye,
  AiOutlineEyeInvisible,
  AiOutlineCopy,
  AiOutlineKey,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import type { DLsiteWork } from "../types";

/** Standalone DRM key cell - manages its own fetch/reveal state to bypass Table memoization */
export function DrmKeyCell({ work, onWorkUpdate }: {
  work: DLsiteWork;
  onWorkUpdate: (workId: string, patch: Partial<DLsiteWork>) => void;
}) {
  const { t } = useTranslation();
  const [isFetching, setIsFetching] = useState(false);
  const [isRevealed, setIsRevealed] = useState(false);

  const handleFetch = async () => {
    setIsFetching(true);
    try {
      const rsp = await BApi.dlsiteWork.getDLsiteWorkDrmKey(work.workId);
      if (!rsp.code) {
        onWorkUpdate(work.workId, { drmKey: rsp.data ?? "" });
      }
    } finally {
      setIsFetching(false);
    }
  };

  const handleCopy = async (key: string) => {
    await navigator.clipboard.writeText(key);
    toast.success(t("resourceSource.dlsite.drmKey.copied"));
  };

  if (work.drmKey === undefined || work.drmKey === null) {
    return (
      <Button
        className="text-xs"
        isLoading={isFetching}
        size="sm"
        startContent={!isFetching ? <AiOutlineKey className="text-lg" /> : undefined}
        variant="light"
        onPress={handleFetch}
      >
        {t("resourceSource.dlsite.drmKey.fetch")}
      </Button>
    );
  }
  if (work.drmKey === "") {
    return (
      <span className="text-xs text-default-400">
        {t("resourceSource.dlsite.drmKey.none")}
      </span>
    );
  }
  return (
    <div className="flex items-center gap-1">
      <span className="text-xs font-mono">
        {isRevealed ? work.drmKey : "******"}
      </span>
      <Tooltip content={isRevealed ? t("resourceSource.dlsite.action.hide") : t("resourceSource.dlsite.action.unhide")}>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          onPress={() => setIsRevealed((v) => !v)}
        >
          {isRevealed
            ? <AiOutlineEyeInvisible className="text-lg" />
            : <AiOutlineEye className="text-lg" />}
        </Button>
      </Tooltip>
      <Tooltip content={t("resourceSource.dlsite.drmKey.copy")}>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          onPress={() => handleCopy(work.drmKey!)}
        >
          <AiOutlineCopy className="text-lg" />
        </Button>
      </Tooltip>
    </div>
  );
}
