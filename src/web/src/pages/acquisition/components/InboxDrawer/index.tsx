"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React from "react";
import { useTranslation } from "react-i18next";
import { Drawer, DrawerBody, DrawerContent, DrawerHeader } from "@heroui/react";

import BApi from "@/sdk/BApi";
import { Button, Chip, Spinner, toast } from "@/components/bakaui";
import { AcquisitionWaitReason } from "@/sdk/constants";

type InboxCandidate =
  components["schemas"]["Bakabase.Service.Components.Acquisition.InboxCandidate"];

interface Props extends DestroyableProps {
  onClaimed?: () => void;
}

/**
 * What is sitting in the download folder, and what each waiting acquisition makes of it.
 *
 * The scores are the watcher's own reasoning, shown rather than hidden: when it declined to claim
 * something, this is where you see that it was torn between two runs — and hand it over yourself.
 */
const InboxDrawer = ({ onClaimed, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [candidates, setCandidates] = React.useState<InboxCandidate[] | null>(null);
  const [busy, setBusy] = React.useState(false);

  const load = React.useCallback(async () => {
    const rsp = await BApi.acquisition.getAcquisitionInbox();

    setCandidates((rsp.data ?? []) as InboxCandidate[]);
  }, []);

  React.useEffect(() => {
    void load();
  }, [load]);

  const claim = async (taskId: number, path: string) => {
    setBusy(true);
    try {
      const rsp = await BApi.acquisition.resumeAcquisition(taskId, {
        signalJson: JSON.stringify({
          reason: AcquisitionWaitReason.WaitingForFile,
          payloadJson: JSON.stringify({ files: [path] }),
        }),
      });

      if (!rsp.code) {
        toast.success(t<string>("acquisition.inbox.claimed"));
        onClaimed?.();
        await load();
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Drawer defaultOpen placement="right" size="lg" onClose={onDestroyed}>
      <DrawerContent>
        <DrawerHeader>{t<string>("acquisition.inbox.title")}</DrawerHeader>
        <DrawerBody>
          {candidates == null ? (
            <div className="flex justify-center py-10">
              <Spinner size="lg" />
            </div>
          ) : candidates.length === 0 ? (
            <div className="text-center text-default-500 py-10">
              {t<string>("acquisition.inbox.empty")}
            </div>
          ) : (
            <div className="flex flex-col gap-3">
              {candidates.map((c) => (
                <div key={c.path} className="border border-default-200 rounded-lg p-2">
                  <div className="flex items-center gap-2">
                    <span className="flex-1 truncate text-sm">{c.fileName}</span>
                    {!c.isStable && (
                      <Chip color="warning" size="sm" variant="flat">
                        {t<string>("acquisition.inbox.stillArriving")}
                      </Chip>
                    )}
                  </div>

                  {c.scores.length === 0 ? (
                    <div className="text-xs text-default-400 mt-1">
                      {t<string>("acquisition.inbox.nothingWaiting")}
                    </div>
                  ) : (
                    <div className="flex flex-col gap-1 mt-1">
                      {c.scores.map((s) => (
                        <div key={s.acquisitionTaskId} className="flex items-center gap-2">
                          <span className="text-xs w-10 tabular-nums text-default-400">
                            {s.score}
                          </span>
                          <span className="flex-1 truncate text-xs">
                            {s.resourceName ??
                              t<string>("acquisition.unnamed", { id: s.acquisitionTaskId })}
                          </span>
                          <Button
                            isDisabled={busy || !c.isStable}
                            size="sm"
                            variant="flat"
                            onPress={() => claim(s.acquisitionTaskId, c.path)}
                          >
                            {t<string>("acquisition.inbox.giveItTo")}
                          </Button>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          )}
        </DrawerBody>
      </DrawerContent>
    </Drawer>
  );
};

export default InboxDrawer;
