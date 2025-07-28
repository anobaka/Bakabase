"use client";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";

import Dependency from "./components/Dependency";

import "./index.scss";
import AppInfo from "@/pages/configuration/components/AppInfo";
import ContactUs from "@/pages/configuration/components/ContactUs";
import Functional from "@/pages/configuration/components/Functional";
import Others from "@/pages/configuration/components/Others";
import BApi from "@/sdk/BApi";

import type { BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo } from "@/sdk/Api";

import Development from "@/pages/configuration/components/Development";

const ConfigurationPage = function (props) {
  const { t } = useTranslation();
  const [appInfo, setAppInfo] = useState<
    Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>
  >({});

  useEffect(() => {
    BApi.app.getAppInfo().then((a) => {
      setAppInfo(a.data || {});
    });
  }, []);

  const applyPatches = (API, patches = {}, success = (rsp) => {}) => {
    API(patches).then((a) => {
      if (!a.code) {
        toast.success(t<string>("Saved"));
        success(a);
      }
    });
  };

  return (
    <div className={"configuration-page"}>
      <Dependency />
      <Functional applyPatches={applyPatches} />
      <Others applyPatches={applyPatches} />
      <AppInfo appInfo={appInfo} />
      <Development />
      <ContactUs />
    </div>
  );
};

export default ConfigurationPage;
