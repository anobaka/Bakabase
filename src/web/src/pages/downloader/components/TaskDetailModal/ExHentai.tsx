"use client";

import type { ThirdPartyFormComponentProps } from "./models";

import _ from "lodash";
import { useTranslation } from "react-i18next";
import React from "react";

import { Input, Textarea } from "@/components/bakaui";
import { ExHentaiDownloadTaskType } from "@/sdk/constants";
import PageRange from "@/pages/downloader/components/TaskDetailModal/PageRange";
import OptionsBasedDownloadPathSelector from "@/pages/downloader/components/TaskDetailModal/OptionsBasedDownloadPathSelector";
import { useExHentaiOptionsStore } from "@/models/options";

type Props = ThirdPartyFormComponentProps<ExHentaiDownloadTaskType>;

export default ({ type, form, onChange, isReadOnly }: Props) => {
  const { t } = useTranslation();
  const knMap = form?.keyAndNames ?? {};

  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);

  const onChangeInternal = (v: typeof knMap) => {
    onChange({
      keyAndNames: v,
    });
  };

  const renderOptions = () => {
    switch (type as ExHentaiDownloadTaskType) {
      case ExHentaiDownloadTaskType.SingleWork:
      case ExHentaiDownloadTaskType.Torrent:
        return (
          <>
            <div>{t<string>("Gallery url(s)")}</div>
            <Textarea
              // label={t<string>('Gallery url(s)')}
              isReadOnly={isReadOnly}
              placeholder={`https://exhentai.org/g/xxxxx/xxxxx/
https://exhentai.org/g/xxxxx/xxxxx/
...`}
              value={_.keys(knMap).join("\n")}
              onValueChange={(v) => {
                onChangeInternal?.(
                  _.fromPairs(v.split("\n").map((line) => [line, null])),
                );
              }}
            />
          </>
        );
      case ExHentaiDownloadTaskType.Watched:
        // todo: https://exhentai.org/watched
        return (
          <>
            <div>{t<string>("Watched page url")}</div>
            <Input
              isReadOnly={isReadOnly}
              placeholder={"https://exhentai.org/watched"}
              value={_.keys(knMap)[0]}
              onValueChange={(v) => {
                onChangeInternal?.(_.fromPairs([[v, null]]));
              }}
            />
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
      case ExHentaiDownloadTaskType.List:
        return (
          <>
            <div>{t<string>("List page url")}</div>
            <Input
              // label={}
              isReadOnly={isReadOnly}
              placeholder={"https://exhentai.org/xxxxxxx"}
              value={_.keys(knMap)[0]}
              onValueChange={(v) => {
                onChangeInternal?.(_.fromPairs([[v, null]]));
              }}
            />
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
      default:
        return null;
    }
  };

  return (
    <>
      {renderOptions()}
      <OptionsBasedDownloadPathSelector
        downloadPath={form?.downloadPath}
        options={exhentaiOptions}
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
