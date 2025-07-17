"use client";
import { useEffect, useRef } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";

import { toast } from "../bakaui";

import { buildLogger, uuidv4 } from "@/components/utils";
import envConfig from "@/config/env";

// 导入所有需要的 zustand store
import { useBackgroundTasksStore } from "@/models/backgroundTasks";
import { useDownloadTasksStore } from "@/models/downloadTasks";
import { useDependentComponentContextsStore } from "@/models/dependentComponentContexts";
import { useFileMovingProgressesStore } from "@/models/fileMovingProgresses";
import { useAppContextStore } from "@/models/appContext";
import { useBulkModificationInternalsStore } from "@/models/bulkModificationInternals";
import { useBTasksStore } from "@/models/bTasks";
import { usePostParserTasksStore } from "@/models/postParserTasks";
import { useIwFsEntryChangeEventsStore } from "@/models/iwFsEntryChangeEvents";
import { useAppUpdaterStateStore } from "@/models/appUpdaterState";
import { optionsStores } from "@/models/options";

const hubEndpoint = `${envConfig.apiEndpoint}/hub/ui`;

export const UIHubConnection = () => {
  const log = buildLogger(`UIHubConnection:${uuidv4()}`);

  // 只初始化一次
  const connRef = useRef<any>(null);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(hubEndpoint)
      .configureLogging(LogLevel.Debug)
      .build();

    conn.onclose(async () => {
      await start();
    });

    // 事件绑定
    conn.on("GetData", (key, data) => {
      log("GetData", key, data);
      switch (key) {
        case "BackgroundTask":
          useBackgroundTasksStore.getState().setTasks(data);
          break;
        case "DownloadTask":
          useDownloadTasksStore.getState().setTasks(data);
          break;
        case "DependentComponentContext":
          useDependentComponentContextsStore.getState().setContexts(data);
          break;
        case "FileMovingProgress":
          useFileMovingProgressesStore.getState().setProgresses(data);
          break;
        case "AppContext":
          useAppContextStore.getState().update(data);
          break;
        case "BulkModificationInternals":
          useBulkModificationInternalsStore.getState().update(data);
          break;
        case "BTask":
          useBTasksStore.getState().setTasks(data);
          break;
        case "PostParserTask":
          usePostParserTasksStore.getState().setTasks(data);
          break;
      }
    });

    conn.on("GetIncrementalData", (key, data) => {
      log("GetIncrementalData", key, data);
      // 你可以根据需要补充增量更新逻辑
    });

    conn.on("DeleteData", (key, id) => {
      if (key === "PostParserTask") {
        usePostParserTasksStore.getState().deleteTask(id);
      }
    });
    conn.on("DeleteAllData", (key) => {
      if (key === "PostParserTask") {
        usePostParserTasksStore.getState().deleteAll();
      }
    });

    conn.on("GetResponse", (rsp) => {
      if (rsp.code == 0) {
        toast.success("Success");
      } else {
        toast.danger(`[${rsp.code}]${rsp.message}`);
      }
    });

    conn.on("IwFsEntriesChange", (events) => {
      useIwFsEntryChangeEventsStore.getState().addRange(events);
    });

    conn.on("OptionsChanged", (name, options) => {
      if (name.toLowerCase() === "uioptions") name = "uiOptions";
      log("options changed", name, options);
      const store = optionsStores[name as keyof typeof optionsStores];

      if (store) {
        store.getState().update(options);
      }
    });

    conn.on("GetAppUpdaterState", (state) => {
      useAppUpdaterStateStore.getState().update(state);
    });

    async function onConnected() {
      await conn.send("GetInitialData");
    }

    async function start() {
      try {
        await conn.start();
        await onConnected();
      } catch (err) {
        log(err);
        setTimeout(() => start(), 5000);
      }
    }

    start();
    connRef.current = conn;

    return () => {
      conn.stop();
    };
    // eslint-disable-next-line
  }, []);

  return null; // 这是一个无 UI 的连接管理组件
};
