"use client";

import type { ThirdPartyFormComponentProps } from "./models";
import type { components } from "@/sdk/BApi2";

import { useTranslation } from "react-i18next";
import React, { useEffect, useState } from "react";

import OptionsBasedDownloadPathSelector from "./OptionsBasedDownloadPathSelector";

import {
  Alert,
  Button,
  Checkbox,
  CheckboxGroup,
  Spinner,
} from "@/components/bakaui";
import {
  BilibiliDownloadTaskType,
  DependentComponentStatus,
} from "@/sdk/constants";
import PageRange from "@/pages/downloader/components/TaskDetailModal/PageRange";
import { useBilibiliOptionsStore } from "@/models/options";
import { useDependentComponentContextsStore } from "@/models/dependentComponentContexts";
import BApi from "@/sdk/BApi";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";
import Configurations from "@/pages/downloader/components/Configurations";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = ThirdPartyFormComponentProps<BilibiliDownloadTaskType>;

type Favorites = Omit<
  components["schemas"]["Bakabase.Modules.ThirdParty.ThirdParties.Bilibili.Models.Favorites"],
  "id"
> & {
  id: string;
};

export default ({ type, form, onChange, isReadOnly }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [loadingFavorites, setLoadingFavorites] = useState(false);
  const [favorites, setFavorites] = useState<Favorites[]>([]);

  const bilibiliOptions = useBilibiliOptionsStore((state) => state.data);

  const dependentComponentContexts = useDependentComponentContextsStore(
    (state) => state.contexts,
  );
  const luxState = dependentComponentContexts?.find(
    (d) => d.id == dependentComponentIds.Lux,
  );
  const ffmpegState = dependentComponentContexts?.find(
    (d) => d.id == dependentComponentIds.FFMpeg,
  );
  const missComponents =
    luxState?.status != DependentComponentStatus.Installed ||
    ffmpegState?.status != DependentComponentStatus.Installed;

  useEffect(() => {
    loadFavorites();
  }, []);

  const loadFavorites = async () => {
    setLoadingFavorites(true);
    try {
      const r = await BApi.bilibili.getBiliBiliFavorites();

      if (!r.code) {
        setFavorites(
          (r.data || []).map((f) => ({
            ...f,
            id: f.id.toString(),
          })),
        );
      }
    } finally {
      setLoadingFavorites(false);
    }
  };

  const renderOptions = () => {
    switch (type as BilibiliDownloadTaskType) {
      case BilibiliDownloadTaskType.Favorites: {
        return (
          <>
            <div>{t<string>("Favorites")}</div>
            <div>
              {loadingFavorites ? (
                <Spinner size="sm" />
              ) : favorites.length === 0 ? (
                <div className={""}>
                  {t<string>(
                    "Unable to retrieve Bilibili favorites. Please ensure your cookie is correctly set and that you have at least one favorite created.",
                  )}
                  <Button
                    color={"primary"}
                    size={"sm"}
                    variant={"light"}
                    onPress={() => {
                      createPortal(Configurations, {
                        onSubmitted: async () => {
                          await loadFavorites();
                        },
                      });
                    }}
                  >
                    {t<string>("Setup now")}
                  </Button>
                </div>
              ) : (
                <CheckboxGroup
                  // color="secondary"
                  isDisabled={isReadOnly}
                  label={t<string>("Select favorites")}
                  orientation="horizontal"
                  size={"sm"}
                  onChange={(values) => {
                    const kn = form?.keyAndNames || {};

                    if (values.length === 0) {
                      Object.keys(kn).forEach((k) => delete kn[k]);
                    } else {
                      favorites.forEach((f) => {
                        if (values.includes(f.id)) {
                          kn[f.id] = f.title;
                        } else {
                          delete kn[f.id];
                        }
                      });
                    }
                    onChange({
                      ...form,
                      keyAndNames: kn,
                    });
                  }}
                >
                  {favorites.map((f) => {
                    return (
                      <Checkbox value={f.id.toString()}>
                        {f.title}({f.mediaCount})
                      </Checkbox>
                    );
                  })}
                </CheckboxGroup>
              )}
            </div>
            <PageRange
              end={form?.endPage}
              start={form?.startPage}
              onChange={(s, e) => {
                onChange?.({
                  startPage: s,
                  endPage: e,
                });
              }}
            />
          </>
        );
      }
      default:
        return null;
    }
  };

  if (missComponents) {
    return (
      <Alert
        // className={'col-span-2'}
        className={"col-start-2"}
        color={"danger"}
        title={t<string>(
          "This function is not working because lux or ffmpeg is not found, check them in system configurations",
        )}
      />
    );
  }

  return (
    <>
      {renderOptions()}
      <OptionsBasedDownloadPathSelector
        downloadPath={form?.downloadPath}
        options={bilibiliOptions}
        onChange={(dp) =>
          onChange({
            ...form,
            downloadPath: dp,
          })
        }
      />
    </>
  );
};
