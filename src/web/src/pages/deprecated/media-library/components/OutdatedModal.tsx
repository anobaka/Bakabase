import type { IdName } from "@/components/types.ts";

import { forwardRef, useCallback, useImperativeHandle, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, Modal, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBTasksStore } from "@/stores/bTasks.ts";
import { BTaskStatus } from "@/sdk/constants.ts";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const SyncTaskPrefix = "SyncMediaLibrary_";
const BuildTaskId = (id: number) => `${SyncTaskPrefix}${id}`;

const OutdatedModal = forwardRef<{ check: () => Promise<void> }>(({}, ref) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [mediaLibraries, setMediaLibraries] = useState<IdName[]>([]);
  const bTasks = useBTasksStore((state) => state.tasks);

  useImperativeHandle(
    ref,
    () => ({
      check: async () => {
        const data = (await BApi.mediaLibraryV2.getOutdatedMediaLibrariesV2())?.data ?? [];

        setMediaLibraries(data);
      },
    }),
    [],
  );

  const unhandledMediaLibraries = mediaLibraries.filter((ml) => {
    const taskId = BuildTaskId(ml.id);

    var hasUnfinishedTask = bTasks.some(
      (x) =>
        x.id == taskId &&
        (x.status == BTaskStatus.Paused ||
          x.status == BTaskStatus.NotStarted ||
          x.status == BTaskStatus.Running),
    );

    return !hasUnfinishedTask;
  });

  const mlIds = unhandledMediaLibraries.map((ml) => ml.id);
  const close = useCallback(() => {
    setMediaLibraries([]);
  }, []);

  return (
    <Modal
      footer={
        <div className="flex items-center gap-2">
          <Button
            color="primary"
            onPress={async () => {
              for (const uml of unhandledMediaLibraries) {
                await BApi.mediaLibraryV2.syncMediaLibraryV2(uml.id);
              }
              close();
            }}
          >
            {t<string>("Sync now")}
          </Button>
          <Button color="default" onPress={close}>
            {t<string>("Later")}
          </Button>
          <Button
            color="warning"
            onPress={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Mark as synced"),
                children: t<string>("Are you sure you want to mark these libraries as synced?"),
                onOk: async () => {
                  await BApi.mediaLibraryV2.markMediaLibraryV2AsSynced(mlIds);
                  toast.success(t<string>("Marked as synced"));
                  close();
                },
              });
            }}
          >
            {t<string>("Mark as synced")}
          </Button>
        </div>
      }
      visible={unhandledMediaLibraries.length > 0}
      onClose={close}
    >
      <div className={"flex flex-col gap-1"}>
        <div>
          {t<string>("Following media libraries may be outdated and should be synchronized:")}
        </div>
        <div className="flex flex-wrap gap-1">
          {unhandledMediaLibraries.map((ml) => (
            <Chip key={ml.id}>{ml.name}</Chip>
          ))}
        </div>
        <div>{t<string>("Resource will be displayed only after synchronization is complete.")}</div>
        <div>{t<string>("Would you like to synchronize now, or do it later manually?")}</div>
      </div>
    </Modal>
  );
});

export default OutdatedModal;
