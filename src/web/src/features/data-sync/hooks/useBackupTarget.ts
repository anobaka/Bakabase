import { useEffect, useState } from "react";

import { useDataSyncStore } from "../stores/dataSync";

import BApi from "@/sdk/BApi";

/*
 * Where a backup before a destructive decision goes, and about how big it is (spec §8.10.4):
 * the size is the database file's, from the overview; the folder is the server's backups folder,
 * from `/app/info` — the data sync records carry no path (their secret canary allows none).
 */

let folder: Promise<string | undefined> | undefined;

/** The backups folder of the server this window shows, read once; unknown where it cannot be read. */
export const readBackupFolder = (): Promise<string | undefined> => {
  folder ??= BApi.app.getAppInfo({ showErrorToast: false }).then(
    (response) => response?.data?.backupPath || undefined,
    () => {
      // Read again next time: a failed read is not an answer.
      folder = undefined;

      return undefined;
    },
  );

  return folder;
};

/** For tests: forgets the folder read. */
export const forgetBackupFolder = () => {
  folder = undefined;
};

const units = ["B", "KB", "MB", "GB", "TB"];

/** A size the way the backup line says it: "50 MB", "1.2 GB". */
export const formatBytes = (bytes: number) => {
  let value = Math.max(0, bytes);
  let unit = 0;

  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  return `${unit === 0 || value >= 10 ? Math.round(value) : value.toFixed(1)} ${units[unit]}`;
};

export interface BackupTarget {
  /** "about {size}", unknown until the overview is read. */
  size?: string;
  folder?: string;
}

export function useBackupTarget(): BackupTarget {
  const bytes = useDataSyncStore((state) => state.overview?.databaseBytes);
  const [path, setPath] = useState<string>();

  useEffect(() => {
    let live = true;

    void readBackupFolder().then((value) => {
      if (live) setPath(value);
    });

    return () => {
      live = false;
    };
  }, []);

  return { size: bytes ? formatBytes(bytes) : undefined, folder: path };
}
