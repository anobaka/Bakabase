"use client";

import { useDLsiteOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

import DownloaderOptionsConfig from "./DownloaderOptionsConfig";

export default function DLsiteConfig() {
  const options = useDLsiteOptionsStore((state) => state.data);

  return (
    <DownloaderOptionsConfig
      options={options}
      patchApi={BApi.options.patchDLsiteOptions}
    />
  );
}
