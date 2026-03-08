"use client";

import { CookieValidatorTarget } from "@/sdk/constants";
import { useExHentaiOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";

import DownloaderOptionsConfig from "./DownloaderOptionsConfig";

export default function ExHentaiConfig() {
  const options = useExHentaiOptionsStore((state) => state.data);

  return (
    <DownloaderOptionsConfig
      cookieValidatorTarget={CookieValidatorTarget.ExHentai}
      options={options}
      patchApi={BApi.options.patchExHentaiOptions}
    />
  );
}
