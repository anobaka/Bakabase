"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle, AiOutlineCheck, AiOutlineClose } from "react-icons/ai";
import { Tooltip, Input, Button, Textarea } from "@heroui/react";

import { Listbox, ListboxItem } from "@/components/bakaui/components/Listbox";
import { toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { CookieValidatorTarget, RuntimeMode } from "@/sdk/constants";
import { useAppContextStore } from "@/stores/appContext";
import {
  useBilibiliOptionsStore,
  useExHentaiOptionsStore,
  useDLsiteOptionsStore,
  usePixivOptionsStore,
  useSoulPlusOptionsStore,
  useBangumiOptionsStore,
  useCienOptionsStore,
  useFanboxOptionsStore,
  useFantiaOptionsStore,
  usePatreonOptionsStore,
  useTmdbOptionsStore,
  useSteamOptionsStore,
} from "@/stores/options";
import {
  DLsiteConfig,
  ExHentaiConfig,
  SteamConfig,
  DownloaderOptionsConfig,
  SimpleThirdPartyConfig,
} from "@/components/ThirdPartyConfig";

export default function ThirdPartyConfigurationPage() {
  const { t } = useTranslation();

  const applyPatches = (
    API: any,
    patches: any = {},
    success: (rsp: any) => void = () => {},
  ) => {
    API(patches).then((a: any) => {
      if (!a.code) {
        toast.success(t<string>("thirdPartyConfig.success.saved"));
        success(a);
      }
    });
  };

  const [exhentaiConfigOpen, setExhentaiConfigOpen] = useState(false);
  const [dlsiteConfigOpen, setDlsiteConfigOpen] = useState(false);
  const [steamConfigOpen, setSteamConfigOpen] = useState(false);
  const [bilibiliConfigOpen, setBilibiliConfigOpen] = useState(false);
  const [pixivConfigOpen, setPixivConfigOpen] = useState(false);
  const [fanboxConfigOpen, setFanboxConfigOpen] = useState(false);
  const [fantiaConfigOpen, setFantiaConfigOpen] = useState(false);
  const [cienConfigOpen, setCienConfigOpen] = useState(false);
  const [patreonConfigOpen, setPatreonConfigOpen] = useState(false);

  const exhentaiOptions = useExHentaiOptionsStore((state) => state.data);
  const dlsiteOptions = useDLsiteOptionsStore((state) => state.data);
  const steamOptions = useSteamOptionsStore((state) => state.data);
  const bilibiliOptions = useBilibiliOptionsStore((state) => state.data);
  const pixivOptions = usePixivOptionsStore((state) => state.data);
  const soulPlusOptions = useSoulPlusOptionsStore((state) => state.data);
  const bangumiOptions = useBangumiOptionsStore((state) => state.data);
  const cienOptions = useCienOptionsStore((state) => state.data);
  const fanboxOptions = useFanboxOptionsStore((state) => state.data);
  const fantiaOptions = useFantiaOptionsStore((state) => state.data);
  const patreonOptions = usePatreonOptionsStore((state) => state.data);
  const tmdbOptions = useTmdbOptionsStore((state) => state.data);

  const [tmpBilibiliOptions, setTmpBilibiliOptions] = useState<any>(
    bilibiliOptions || {},
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
  const [cookieValidationResults, setCookieValidationResults] = useState<{
    [key: string]: "succeed" | "failed";
  }>({});
  const runtimeMode = useAppContextStore((s) => s.runtimeMode);
  const isDesktopApp = runtimeMode !== RuntimeMode.Docker;
  const [capturingCookies, setCapturingCookies] = useState<{ [key: string]: boolean }>({});

  useEffect(() => {
    setTmpBilibiliOptions(JSON.parse(JSON.stringify(bilibiliOptions || {})));
  }, [bilibiliOptions]);
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
          setCookieValidationResults((prev) => ({ ...prev, [thirdParty]: "failed" }));
          toast.danger(`${t<string>("thirdPartyConfig.error.invalidCookie")}:${r.message}`);
        } else {
          setCookieValidationResults((prev) => ({ ...prev, [thirdParty]: "succeed" }));
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
          label={t<string>("thirdPartyConfig.label.cookie")}
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
          label={t<string>("thirdPartyConfig.label.maxConcurrency")}
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
          label={t<string>("thirdPartyConfig.label.requestInterval")}
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
          label={t<string>("thirdPartyConfig.label.defaultPath")}
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
          label={t<string>("thirdPartyConfig.label.namingConvention")}
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
          label={t<string>("thirdPartyConfig.label.maxRetries")}
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
          label={t<string>("thirdPartyConfig.label.requestTimeout")}
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
          {t<string>("thirdPartyConfig.action.save")}
        </Button>
        {options.cookie && (
          <Button
            color={cookieValidationResults[thirdPartyName] === "succeed" ? "success" : cookieValidationResults[thirdPartyName] === "failed" ? "danger" : "default"}
            disabled={
              !options.cookie?.length || validatingCookies[thirdPartyName]
            }
            isLoading={validatingCookies[thirdPartyName]}
            size="sm"
            variant="flat"
            startContent={cookieValidationResults[thirdPartyName] === "succeed" ? <AiOutlineCheck /> : cookieValidationResults[thirdPartyName] === "failed" ? <AiOutlineClose /> : undefined}
            onPress={() => {
              const validatorMap: { [key: string]: CookieValidatorTarget } = {
                bilibili: CookieValidatorTarget.BiliBili,
                exhentai: CookieValidatorTarget.ExHentai,
                pixiv: CookieValidatorTarget.Pixiv,
                fanbox: CookieValidatorTarget.Fanbox,
                fantia: CookieValidatorTarget.Fantia,
                cien: CookieValidatorTarget.Cien,
                patreon: CookieValidatorTarget.Patreon,
              };
              const target = validatorMap[thirdPartyName.toLowerCase()];

              if (target) {
                validateCookie(thirdPartyName, options.cookie, target);
              }
            }}
          >
            {t<string>("thirdPartyConfig.action.validateCookie")}
          </Button>
        )}
        {isDesktopApp && (() => {
          const captureMap: { [key: string]: CookieValidatorTarget } = {
            bilibili: CookieValidatorTarget.BiliBili,
            exhentai: CookieValidatorTarget.ExHentai,
            pixiv: CookieValidatorTarget.Pixiv,
            fanbox: CookieValidatorTarget.Fanbox,
            fantia: CookieValidatorTarget.Fantia,
            cien: CookieValidatorTarget.Cien,
            patreon: CookieValidatorTarget.Patreon,
          };
          const target = captureMap[thirdPartyName.toLowerCase()];
          if (!target) return null;
          return (
            <Button
              color="secondary"
              isLoading={capturingCookies[thirdPartyName]}
              size="sm"
              variant="flat"
              onPress={async () => {
                setCapturingCookies((prev) => ({ ...prev, [thirdPartyName]: true }));
                try {
                  const rsp = await BApi.tool.captureCookie({ target });
                  if (!rsp.code && rsp.data) {
                    setOptions({ ...options, cookie: rsp.data });
                  }
                } finally {
                  setCapturingCookies((prev) => ({ ...prev, [thirdPartyName]: false }));
                }
              }}
            >
              {t("resourceSource.accounts.loginToImport")}
            </Button>
          );
        })()}
      </div>
    </div>
  );

  const thirdPartySettings = useMemo(
    () => [
      {
        key: "bilibili",
        label: "Bilibili",
        tip: "thirdPartyConfig.tip.bilibili",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setBilibiliConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({bilibiliOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Bilibili"
                isOpen={bilibiliConfigOpen}
                onClose={() => setBilibiliConfigOpen(false)}
                accounts={bilibiliOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchBilibiliOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.BiliBili}
                cookieCaptureTarget={CookieValidatorTarget.BiliBili}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={bilibiliOptions} patchApi={BApi.options.patchBilibiliOptions} />
          </div>
        ),
      },
      {
        key: "exhentai",
        label: "ExHentai",
        tip: "thirdPartyConfig.tip.exhentai",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="flat"
                onPress={() => setExhentaiConfigOpen(true)}
              >
                {t("resourceSource.action.configure")} ({exhentaiOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <ExHentaiConfig
                isOpen={exhentaiConfigOpen}
                onClose={() => setExhentaiConfigOpen(false)}
              />
            </div>
            <DownloaderOptionsConfig
              hideCookie
              options={exhentaiOptions}
              patchApi={BApi.options.patchExHentaiOptions}
              cookieValidatorTarget={CookieValidatorTarget.ExHentai}
            />
          </div>
        ),
      },
      {
        key: "steam",
        label: "Steam",
        tip: "thirdPartyConfig.tip.steam",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="flat"
                onPress={() => setSteamConfigOpen(true)}
              >
                {t("resourceSource.action.configure")} ({steamOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SteamConfig
                isOpen={steamConfigOpen}
                onClose={() => setSteamConfigOpen(false)}
              />
            </div>
          </div>
        ),
      },
      {
        key: "pixiv",
        label: "Pixiv",
        tip: "thirdPartyConfig.tip.pixiv",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setPixivConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({pixivOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Pixiv"
                isOpen={pixivConfigOpen}
                onClose={() => setPixivConfigOpen(false)}
                accounts={pixivOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchPixivOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.Pixiv}
                cookieCaptureTarget={CookieValidatorTarget.Pixiv}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={pixivOptions} patchApi={BApi.options.patchPixivOptions} />
          </div>
        ),
      },
      {
        key: "soulplus",
        label: "SoulPlus",
        tip: "thirdPartyConfig.tip.soulplus",
        content: (
          <div className="space-y-4">
            <div>
              <Input
                label={t<string>("thirdPartyConfig.label.cookie")}
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
                label={t<string>("thirdPartyConfig.label.autoBuyThreshold")}
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
                {t<string>("thirdPartyConfig.action.save")}
              </Button>
            </div>
          </div>
        ),
      },
      {
        key: "bangumi",
        label: "Bangumi",
        tip: "thirdPartyConfig.tip.bangumi",
        content: (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("thirdPartyConfig.label.maxConcurrency")}
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
                label={t<string>("thirdPartyConfig.label.requestInterval")}
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
                label={t<string>("thirdPartyConfig.label.cookie")}
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
                label={t<string>("thirdPartyConfig.label.userAgent")}
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
                label={t<string>("thirdPartyConfig.label.referer")}
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
                {t<string>("thirdPartyConfig.action.save")}
              </Button>
            </div>
          </div>
        ),
      },
      {
        key: "cien",
        label: "Cien",
        tip: "thirdPartyConfig.tip.cien",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setCienConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({cienOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Cien"
                isOpen={cienConfigOpen}
                onClose={() => setCienConfigOpen(false)}
                accounts={cienOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchCienOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.Cien}
                cookieCaptureTarget={CookieValidatorTarget.Cien}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={cienOptions} patchApi={BApi.options.patchCienOptions} />
          </div>
        ),
      },
      {
        key: "dlsite",
        label: "DLsite",
        tip: "thirdPartyConfig.tip.dlsite",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button
                size="sm"
                variant="flat"
                onPress={() => setDlsiteConfigOpen(true)}
              >
                {t("resourceSource.action.configure")} ({dlsiteOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <DLsiteConfig
                isOpen={dlsiteConfigOpen}
                onClose={() => setDlsiteConfigOpen(false)}
              />
            </div>
            <DownloaderOptionsConfig
              hideCookie
              options={dlsiteOptions}
              patchApi={BApi.options.patchDLsiteOptions}
            />
          </div>
        ),
      },
      {
        key: "fanbox",
        label: "Fanbox",
        tip: "thirdPartyConfig.tip.fanbox",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setFanboxConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({fanboxOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Fanbox"
                isOpen={fanboxConfigOpen}
                onClose={() => setFanboxConfigOpen(false)}
                accounts={fanboxOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchFanboxOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.Fanbox}
                cookieCaptureTarget={CookieValidatorTarget.Fanbox}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={fanboxOptions} patchApi={BApi.options.patchFanboxOptions} />
          </div>
        ),
      },
      {
        key: "fantia",
        label: "Fantia",
        tip: "thirdPartyConfig.tip.fantia",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setFantiaConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({fantiaOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Fantia"
                isOpen={fantiaConfigOpen}
                onClose={() => setFantiaConfigOpen(false)}
                accounts={fantiaOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchFantiaOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.Fantia}
                cookieCaptureTarget={CookieValidatorTarget.Fantia}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={fantiaOptions} patchApi={BApi.options.patchFantiaOptions} />
          </div>
        ),
      },
      {
        key: "patreon",
        label: "Patreon",
        tip: "thirdPartyConfig.tip.patreon",
        content: (
          <div className="space-y-4">
            <div className="flex items-center gap-2">
              <Button size="sm" variant="flat" onPress={() => setPatreonConfigOpen(true)}>
                {t("resourceSource.action.configure")} ({patreonOptions?.accounts?.length || 0} {t("resourceSource.accounts.title", { platform: "" }).trim()})
              </Button>
              <SimpleThirdPartyConfig
                title="Patreon"
                isOpen={patreonConfigOpen}
                onClose={() => setPatreonConfigOpen(false)}
                accounts={patreonOptions?.accounts || []}
                onSave={(accounts) => BApi.options.patchPatreonOptions({ accounts })}
                cookieValidatorTarget={CookieValidatorTarget.Patreon}
                cookieCaptureTarget={CookieValidatorTarget.Patreon}
              />
            </div>
            <DownloaderOptionsConfig hideCookie options={patreonOptions} patchApi={BApi.options.patchPatreonOptions} />
          </div>
        ),
      },
      {
        key: "tmdb",
        label: "TMDB",
        tip: "thirdPartyConfig.tip.tmdb",
        content: (
          <div className="space-y-4">
            <div className="grid grid-cols-2 gap-4">
              <Input
                label={t<string>("thirdPartyConfig.label.maxConcurrency")}
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
                label={t<string>("thirdPartyConfig.label.requestInterval")}
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
                label={t<string>("thirdPartyConfig.label.apiKey")}
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
                label={t<string>("thirdPartyConfig.label.cookie")}
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
                label={t<string>("thirdPartyConfig.label.userAgent")}
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
                label={t<string>("thirdPartyConfig.label.referer")}
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
                {t<string>("thirdPartyConfig.action.save")}
              </Button>
            </div>
          </div>
        ),
      },
    ],
    [
      tmpBilibiliOptions,
      tmpPixivOptions,
      tmpSoulPlusOptions,
      tmpBangumiOptions,
      tmpCienOptions,
      tmpFanboxOptions,
      tmpFantiaOptions,
      tmpPatreonOptions,
      tmpTmdbOptions,
      exhentaiOptions,
      dlsiteOptions,
      steamOptions,
      bilibiliOptions,
      pixivOptions,
      fanboxOptions,
      fantiaOptions,
      cienOptions,
      patreonOptions,
      exhentaiConfigOpen,
      dlsiteConfigOpen,
      steamConfigOpen,
      bilibiliConfigOpen,
      pixivConfigOpen,
      fanboxConfigOpen,
      fantiaConfigOpen,
      cienConfigOpen,
      patreonConfigOpen,
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
