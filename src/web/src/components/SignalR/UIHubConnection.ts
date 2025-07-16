"use client";
import { useEffect, useRef } from "react";
import { HubConnectionBuilder, HubConnectionState, LogLevel } from "@microsoft/signalr";

import { toast } from "../bakaui";

import { buildLogger } from "@/components/utils";
import delay from "delay";
import envConfig from "@/config/env";
import { v4 as uuidv4 } from "uuid";

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
  // 只初始化一次
  const connRef = useRef<any>(null);
  const logRef = useRef(buildLogger(`UIHubConnection:${uuidv4()}`));
  const log = logRef.current;
  const isRunningRef = useRef(true);

  useEffect(() => {
    const conn = new HubConnectionBuilder()
      .withUrl(hubEndpoint)
      .configureLogging(LogLevel.Information)
      .build();

    // 事件绑定
    conn.on("GetData", (key, data) => {
      log("GetData", key, data);
      switch (key) {
        case "BackgroundTask":
          useBackgroundTasksStore.getState().setTasks(data);
          break;
        case "DownloadTaskDbModel":
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

    conn.on('GetIncrementalData', (key, data) => {
      log("GetIncrementalData", key, data);
      switch (key) {
        case 'BackgroundTask':
          useBackgroundTasksStore.getState().updateTask(data);
          break;
        case 'DownloadTask':
          useDownloadTasksStore.getState().updateTask(data);
          break;
        case 'DependentComponentContext':
          useDependentComponentContextsStore.getState().updateContext(data);
          break;
        case 'FileMovingProgress':
          useFileMovingProgressesStore.getState().updateProgress(data);
          break;
        case 'BTask':
          useBTasksStore.getState().updateTask(data);
          break;
        case 'PostParserTask':
          usePostParserTasksStore.getState().updateTask(data);
          break;
      }
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
      log('connected');
      await conn.send("GetInitialData");
    }
    
    // 监听连接关闭事件
    conn.onclose(async () => {
      log('connection closed, attempting to reconnect...');
    });

    // 后台守护循环 - 每5秒检查一次连接状态
    const guardLoop = async () => {
      while (isRunningRef.current) {
        try {
          if (conn.state === HubConnectionState.Disconnected) {
            log('connection disconnected, attempting to connect...');
            try {
              await conn.start();
              await onConnected();
            } catch (err) {
              log('start failed:', err);
            }
          }
        } catch (err) {
          log('guard loop error:', err);
        } finally {
          await delay(5000);
        }
      }
    };
    guardLoop();

    connRef.current = conn;

    return () => {
      isRunningRef.current = false;
      conn.stop();
    };
  }, []);

  return null;
};
