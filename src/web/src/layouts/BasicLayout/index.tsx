"use client";

import React, { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { TourProvider } from "@reactour/tour";
import { useTranslation } from "react-i18next";

import styles from "./index.module.scss";
import PageNav from "./components/PageNav";

import { InitializationContentType } from "@/sdk/constants";
import FloatingAssistant from "@/components/FloatingAssistant";
import { ErrorBoundary } from "@/components/Error";
import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Modal } from "@/components/bakaui";

const log = buildLogger("BasicLayout");

export default function BasicLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();

  useEffect(() => {
    log("Initializing...");
    BApi.app.checkAppInitialized().then((a) => {
      switch (a.data) {
        case InitializationContentType.NotAcceptTerms:
          navigate("/welcome");
          break;
        case InitializationContentType.NeedRestart:
          createPortal(Modal, {
            title: t<string>("Please restart app and try this later"),
            footer: false,
          });
          break;
      }
    });
  }, []);

  return (
    <TourProvider steps={[]}>
      <ErrorBoundary>
        <div className={styles.insideWorld}>
          <FloatingAssistant />
          <PageNav />
          <div className={`${styles.main} pt-1 pb-1 pr-1`}>{children}</div>
        </div>
      </ErrorBoundary>
    </TourProvider>
  );
}
