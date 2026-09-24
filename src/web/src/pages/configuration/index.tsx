"use client";

import type { BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo } from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineSearch } from "react-icons/ai";

import Dependency from "./components/Dependency";

import "./index.scss";
import AppInfo from "@/pages/configuration/components/AppInfo";
import ContactUs from "@/pages/configuration/components/ContactUs";
import Functional from "@/pages/configuration/components/Functional";
import Others from "@/pages/configuration/components/Others";
import Development from "@/pages/configuration/components/Development";
import RemoteAccess from "@/pages/configuration/components/RemoteAccess";
import {
  normalizeQuery,
  SettingsSearchResults,
} from "@/pages/configuration/components/SettingsSection";
import { Input } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const ConfigurationPage: React.FC = () => {
  const { t } = useTranslation();
  const [appInfo, setAppInfo] = useState<
    Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>
  >({});
  const [keyword, setKeyword] = useState("");

  const query = useMemo(() => normalizeQuery(keyword), [keyword]);

  useEffect(() => {
    BApi.app.getAppInfo().then((a) => {
      setAppInfo(a.data || {});
    });
  }, []);

  const applyPatches = <T,>(
    api: (patches: T) => Promise<{ code?: number }>,
    patches: T,
    success?: (rsp: { code?: number }) => void,
  ) => {
    api(patches).then((a) => {
      if (!a.code) {
        toast.success(t("common.success.saved"));
        success?.(a);
      }
    });
  };

  return (
    <div className="configuration-page flex flex-col gap-3">
      <div className="sticky top-0 z-10 py-2 bg-background/80 backdrop-blur">
        <Input
          isClearable
          className="max-w-[420px]"
          placeholder={t<string>("configuration.search.placeholder")}
          size="sm"
          startContent={<AiOutlineSearch className="text-base" />}
          value={keyword}
          onValueChange={setKeyword}
        />
      </div>

      {/* Each section filters itself and renders nothing when it has no match; they
          report that back so the page can show an empty state without knowing what
          any of them contains. */}
      <SettingsSearchResults>
        {(anyMatched) => (
          <>
            <Dependency query={query} />
            <Functional applyPatches={applyPatches} query={query} />
            <Others applyPatches={applyPatches} query={query} />
            <RemoteAccess query={query} />
            <AppInfo appInfo={appInfo} applyPatches={applyPatches} query={query} />
            <Development query={query} />
            <ContactUs query={query} />

            {query.length > 0 && !anyMatched && (
              <div className="text-sm text-foreground-400 py-6 text-center">
                {t<string>("configuration.search.noResults", { keyword })}
              </div>
            )}
          </>
        )}
      </SettingsSearchResults>
    </div>
  );
};

export default ConfigurationPage;
