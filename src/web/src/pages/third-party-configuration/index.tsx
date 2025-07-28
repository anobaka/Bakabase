"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";
import { Tooltip, Input, Button, Textarea } from "@heroui/react";

import { Listbox, ListboxItem } from "@/components/bakaui/components/Listbox";
import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { CookieValidatorTarget } from "@/sdk/constants";
import {
  useBilibiliOptionsStore,
  useExHentaiOptionsStore,
  usePixivOptionsStore,
  useSoulPlusOptionsStore,
  useBangumiOptionsStore,
  useCienOptionsStore,
  useDLsiteOptionsStore,
  useFanboxOptionsStore,
  useFantiaOptionsStore,
  usePatreonOptionsStore,
  useTmdbOptionsStore,
} from "@/stores/options";

export default function ThirdPartyConfigurationPage() {
  const { t } = useTranslation();

  const applyPatches = (
    API: any,
    patches: any = {},
    success: (rsp: any) => void = () => {},
  ) => {
    API(patches).then((a: any) => {
      if (!a.code) {
        toast.success(t<string>("Saved"));
        success(a);
      }
    });
  };

  const bilibiliOptions = useBilibiliOptionsStore((state) => state.data);
  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);
  const pixivOptions = usePixivOptionsStore((state) => state.data);
  const soulPlusOptions = useSoulPlusOptionsStore((state) => state.data);
  const bangumiOptions = useBangumiOptionsStore((state) => state.data);
  const cienOptions = useCienOptionsStore((state) => state.data);
  const dlsiteOptions = useDLsiteOptionsStore((state) => state.data);
  const fanboxOptions = useFanboxOptionsStore((state) => state.data);
  const fantiaOptions = useFantiaOptionsStore((state) => state.data);
  const patreonOptions = usePatreonOptionsStore((state) => state.data);
  const tmdbOptions = useTmdbOptionsStore((state) => state.data);

  const [tmpBilibiliOptions, setTmpBilibiliOptions] = useState<any>(
    bilibiliOptions || {},
  );
  const [tmpExHentaiOptions, setTmpExHentaiOptions] = useState<any>(
    exhentaiOptions || {},
  );
  const [tmpPixivOptions, setTmpPixivOptions] = useState<any>(
    pixivOptions || {},
  );
  const [tmpSoulPlusOptions, setTmpSoulPlusOptions] = useState<any>(
    soulPlusOptions || {},
  );
  const [tmpBangumiOptions, setTmpBangumiOptions] = useState<any>(
    bangumiOptions || {},
  );
  const [tmpCienOptions, setTmpCienOptions] = useState<any>(cienOptions || {});
  const [tmpDLsiteOptions, setTmpDLsiteOptions] = useState<any>(
    dlsiteOptions || {},
  );
  const [tmpFanboxOptions, setTmpFanboxOptions] = useState<any>(
    fanboxOptions || {},
  );
  const [tmpFantiaOptions, setTmpFantiaOptions] = useState<any>(
    fantiaOptions || {},
  );
  const [tmpPatreonOptions, setTmpPatreonOptions] = useState<any>(
    patreonOptions || {},
  );
  const [tmpTmdbOptions, setTmpTmdbOptions] = useState<any>(tmdbOptions || {});

  const [validatingCookies, setValidatingCookies] = useState<{
    [key: string]: boolean;
  }>({});

  useEffect(() => {
    setTmpBilibiliOptions(JSON.parse(JSON.stringify(bilibiliOptions || {})));
  }, [bilibiliOptions]);
  useEffect(() => {
    setTmpExHentaiOptions(JSON.parse(JSON.stringify(exhentaiOptions || {})));
  }, [exhentaiOptions]);
  useEffect(() => {
    setTmpPixivOptions(JSON.parse(JSON.stringify(pixivOptions || {})));
  }, [pixivOptions]);
  useEffect(() => {
    setTmpSoulPlusOptions(JSON.parse(JSON.stringify(soulPlusOptions || {})));
  }, [soulPlusOptions]);
  useEffect(() => {
    setTmpBangumiOptions(JSON.parse(JSON.stringify(bangumiOptions || {})));
  }, [bangumiOptions]);
  useEffect(() => {
    setTmpCienOptions(JSON.parse(JSON.stringify(cienOptions || {})));
  }, [cienOptions]);
  useEffect(() => {
    setTmpDLsiteOptions(JSON.parse(JSON.stringify(dlsiteOptions || {})));
  }, [dlsiteOptions]);
  useEffect(() => {
    setTmpFanboxOptions(JSON.parse(JSON.stringify(fanboxOptions || {})));
  }, [fanboxOptions]);
  useEffect(() => {
    setTmpFantiaOptions(JSON.parse(JSON.stringify(fantiaOptions || {})));
  }, [fantiaOptions]);
  useEffect(() => {
    setTmpPatreonOptions(JSON.parse(JSON.stringify(patreonOptions || {})));
  }, [patreonOptions]);
  useEffect(() => {
    setTmpTmdbOptions(JSON.parse(JSON.stringify(tmdbOptions || {})));
  }, [tmdbOptions]);

  const validateCookie = (
    thirdParty: string,
    cookie: string,
    target: CookieValidatorTarget,
  ) => {
    setValidatingCookies((prev) => ({ ...prev, [thirdParty]: true }));
    BApi.tool
      .validateCookie({ cookie, target })
      .then((r: any) => {
        if (r.code) {
          toast.danger(`${t<string>("Invalid cookie")}:${r.message}`);
        } else {
          toast.success(t<string>("Cookie is good"));
        }
      })
      .finally(() => {
        setValidatingCookies((prev) => ({ ...prev, [thirdParty]: false }));
      });
  };

  const renderDownloaderOptions = (
    options: any,
    setOptions: (opts: any) => void,
    saveApi: any,
    thirdPartyName: string,
  ) => (
    <div className="space-y-4">
      <div>
        <Textarea
          label={t<string>("Cookie")}
          size="sm"
          value={options.cookie || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              cookie: v,
            });
          }}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("Max Concurrency")}
          size="sm"
          type="number"
          value={String(options.maxConcurrency || 1)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              maxConcurrency: Number(v) || 1,
            });
          }}
        />
        <Input
          label={t<string>("Request Interval (ms)")}
          size="sm"
          type="number"
          value={String(options.requestInterval || 1000)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              requestInterval: Number(v) || 1000,
            });
          }}
        />
      </div>
      <div>
        <Input
          label={t<string>("Default Path")}
          size="sm"
          value={options.defaultPath || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              defaultPath: v,
            });
          }}
        />
      </div>
      <div>
        <Input
          label={t<string>("Naming Convention")}
          size="sm"
          value={options.namingConvention || ""}
          onValueChange={(v) => {
            setOptions({
              ...options,
              namingConvention: v,
            });
          }}
        />
      </div>
      <div className="grid grid-cols-2 gap-4">
        <Input
          label={t<string>("Max Retries")}
          size="sm"
          type="number"
          value={String(options.maxRetries || 0)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              maxRetries: Number(v) || 0,
            });
          }}
        />
        <Input
          label={t<string>("Request Timeout (ms)")}
          size="sm"
          type="number"
          value={String(options.requestTimeout || 0)}
          onValueChange={(v) => {
            setOptions({
              ...options,
              requestTimeout: Number(v) || 0,
            });
          }}
        />
      </div>
      <div className="operations flex gap-2">
        <Button
          color="primary"
          size="sm"
          onPress={() => {
            applyPatches(saveApi, options);
          }}
        >
          {t<string>("Save")}
        </Button>
        {options.cookie && (
          <Button
            disabled={
              !options.cookie?.length || validatingCookies[thirdPartyName]
            }
            isLoading={validatingCookies[thirdPartyName]}
            size="sm"
            onPress={() => {
              const validatorMap: { [key: string]: CookieValidatorTarget } = {
                bilibili: CookieValidatorTarget.BiliBili,
                exhentai: CookieValidatorTarget.ExHentai,
                pixiv: CookieValidatorTarget.Pixiv,
              };
              const target = validatorMap[thirdPartyName.toLowerCase()];

              if (target) {
                validateCookie(thirdPartyName, options.cookie, target);
              }
            }}
          >
            {t<string>("Validate cookie")}
          </Button>
        )}
      </div>
    </div>
  );

  const thirdPartySettings = useMemo(
    () => [
      {
        key: "bilibili",
        label: "Bilibili",
        tip: "Configure Bilibili downloader settings including cookie authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpBilibiliOptions,
          setTmpBilibiliOptions,
          BApi.options.patchBilibiliOptions,
          "bilibili",
        ),
      },
      {
        key: "exhentai",
        label: "ExHentai",
        tip: "Configure ExHentai downloader settings. Cookie is required for authentication.",
        content: renderDownloaderOptions(
          tmpExHentaiOptions,
          setTmpExHentaiOptions,
          BApi.options.patchExHentaiOptions,
          "exhentai",
        ),
      },
      {
        key: "pixiv",
        label: "Pixiv",
        tip: "Configure Pixiv downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpPixivOptions,
          setTmpPixivOptions,
          BApi.options.patchPixivOptions,
          "pixiv",
        ),
      },
      {
        key: "soulplus",
        label: "SoulPlus",
        tip: "Configure SoulPlus settings including cookie authentication and auto-buy threshold.",
        content: (
          <div className="space-y-4">
            <div>
              <Input
                label={t<string>("Cookie")}
                size="sm"
                value={tmpSoulPlusOptions.cookie || ""}
                onValueChange={(v) => {
                  setTmpSoulPlusOptions({
                    ...tmpSoulPlusOptions,
                    cookie: v,
                  });
                }}
              />
            </div>
            <div>
              <Input
                label={t<string>("Auto Buy Threshold")}
                size="sm"
                type="number"
                value={String(tmpSoulPlusOptions.autoBuyThreshold || 0)}
                onValueChange={(v) => {
                  setTmpSoulPlusOptions({
                    ...tmpSoulPlusOptions,
                    autoBuyThreshold: Number(v) || 0,
                  });
                }}
              />
            </div>
            <div className="operations">
              <Button
                color="primary"
                size="sm"
                onPress={() => {
                  applyPatches(
                    BApi.options.patchSoulPlusOptions,
                    tmpSoulPlusOptions,
                  );
                }}
              >
                {t<string>("Save")}
              </Button>
            </div>
          </div>
        ),
      },
      {
        key: "bangumi",
        label: "Bangumi",
        tip: "Configure Bangumi settings including request parameters and authentication.",
        content: (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("Max Concurrency")}
                size="sm"
                type="number"
                value={String(tmpBangumiOptions.maxConcurrency || 1)}
                onValueChange={(v) => {
                  setTmpBangumiOptions({
                    ...tmpBangumiOptions,
                    maxConcurrency: Number(v) || 1,
                  });
                }}
              />
              <Input
                label={t<string>("Request Interval (ms)")}
                size="sm"
                type="number"
                value={String(tmpBangumiOptions.requestInterval || 1000)}
                onValueChange={(v) => {
                  setTmpBangumiOptions({
                    ...tmpBangumiOptions,
                    requestInterval: Number(v) || 1000,
                  });
                }}
              />
            </div>
            <div>
              <Input
                label={t<string>("Cookie")}
                size="sm"
                value={tmpBangumiOptions.cookie || ""}
                onValueChange={(v) => {
                  setTmpBangumiOptions({
                    ...tmpBangumiOptions,
                    cookie: v,
                  });
                }}
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("User Agent")}
                size="sm"
                value={tmpBangumiOptions.userAgent || ""}
                onValueChange={(v) => {
                  setTmpBangumiOptions({
                    ...tmpBangumiOptions,
                    userAgent: v,
                  });
                }}
              />
              <Input
                label={t<string>("Referer")}
                size="sm"
                value={tmpBangumiOptions.referer || ""}
                onValueChange={(v) => {
                  setTmpBangumiOptions({
                    ...tmpBangumiOptions,
                    referer: v,
                  });
                }}
              />
            </div>
            <div className="operations">
              <Button
                color="primary"
                size="sm"
                onPress={() => {
                  applyPatches(
                    BApi.options.patchBangumiOptions,
                    tmpBangumiOptions,
                  );
                }}
              >
                {t<string>("Save")}
              </Button>
            </div>
          </div>
        ),
      },
      {
        key: "cien",
        label: "Cien",
        tip: "Configure Cien downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpCienOptions,
          setTmpCienOptions,
          BApi.options.patchCienOptions,
          "cien",
        ),
      },
      {
        key: "dlsite",
        label: "DLsite",
        tip: "Configure DLsite downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpDLsiteOptions,
          setTmpDLsiteOptions,
          BApi.options.patchDLsiteOptions,
          "dlsite",
        ),
      },
      {
        key: "fanbox",
        label: "Fanbox",
        tip: "Configure Fanbox downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpFanboxOptions,
          setTmpFanboxOptions,
          BApi.options.patchFanboxOptions,
          "fanbox",
        ),
      },
      {
        key: "fantia",
        label: "Fantia",
        tip: "Configure Fantia downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpFantiaOptions,
          setTmpFantiaOptions,
          BApi.options.patchFantiaOptions,
          "fantia",
        ),
      },
      {
        key: "patreon",
        label: "Patreon",
        tip: "Configure Patreon downloader settings including authentication and download parameters.",
        content: renderDownloaderOptions(
          tmpPatreonOptions,
          setTmpPatreonOptions,
          BApi.options.patchPatreonOptions,
          "patreon",
        ),
      },
      {
        key: "tmdb",
        label: "TMDB",
        tip: "Configure TMDB (The Movie Database) settings including API key and request parameters.",
        content: (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("Max Concurrency")}
                size="sm"
                type="number"
                value={String(tmpTmdbOptions.maxConcurrency || 1)}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    maxConcurrency: Number(v) || 1,
                  });
                }}
              />
              <Input
                label={t<string>("Request Interval (ms)")}
                size="sm"
                type="number"
                value={String(tmpTmdbOptions.requestInterval || 1000)}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    requestInterval: Number(v) || 1000,
                  });
                }}
              />
            </div>
            <div>
              <Input
                label={t<string>("API Key")}
                size="sm"
                value={tmpTmdbOptions.apiKey || ""}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    apiKey: v,
                  });
                }}
              />
            </div>
            <div>
              <Textarea
                label={t<string>("Cookie")}
                size="sm"
                value={tmpTmdbOptions.cookie || ""}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    cookie: v,
                  });
                }}
              />
            </div>
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("User Agent")}
                size="sm"
                value={tmpTmdbOptions.userAgent || ""}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    userAgent: v,
                  });
                }}
              />
              <Input
                label={t<string>("Referer")}
                size="sm"
                value={tmpTmdbOptions.referer || ""}
                onValueChange={(v) => {
                  setTmpTmdbOptions({
                    ...tmpTmdbOptions,
                    referer: v,
                  });
                }}
              />
            </div>
            <div className="operations">
              <Button
                color="primary"
                size="sm"
                onPress={() => {
                  applyPatches(BApi.options.patchTmdbOptions, tmpTmdbOptions);
                }}
              >
                {t<string>("Save")}
              </Button>
            </div>
          </div>
        ),
      },
    ],
    [
      tmpBilibiliOptions,
      tmpExHentaiOptions,
      tmpPixivOptions,
      tmpSoulPlusOptions,
      tmpBangumiOptions,
      tmpCienOptions,
      tmpDLsiteOptions,
      tmpFanboxOptions,
      tmpFantiaOptions,
      tmpPatreonOptions,
      tmpTmdbOptions,
    ],
  );

  return (
    <div className="flex gap-6">
      <div className="w-32 shrink-0 sticky top-4 self-start border-small px-1 py-2 rounded-small border-default-200 dark:border-default-100">
        <Listbox
          aria-label="Third party list"
          selectionMode="single"
          onAction={(key) => {
            const el = document.getElementById(`tp-${String(key)}`);

            if (el) {
              el.scrollIntoView({ behavior: "smooth", block: "start" });
            }
          }}
        >
          {thirdPartySettings.map((s) => (
            <ListboxItem key={s.key}>{s.label}</ListboxItem>
          ))}
        </Listbox>
      </div>
      <div className="flex-1 space-y-8">
        {thirdPartySettings.map((s) => (
          <section key={s.key} className="scroll-mt-16" id={`tp-${s.key}`}>
            <div className="flex items-center gap-2 mb-2 text-lg font-semibold">
              <span>{s.label}</span>
              {s.tip && (
                <Tooltip
                  color="secondary"
                  content={t<string>(s.tip)}
                  placement="top"
                >
                  <AiOutlineQuestionCircle className="text-base" />
                </Tooltip>
              )}
            </div>
            <div>{s.content}</div>
          </section>
        ))}
      </div>
    </div>
  );
}
