import React, { useEffect, useState } from "react";
import ReactDOM from "react-dom/client";
import { HashRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

import "@/styles/globals.css";
import {
  getPreviewOptions,
  resetPreview,
  setPreviewScenario,
  type PreviewScenario,
} from "./mockApi";

import i18n from "@/i18n";
import BakabaseContextProvider from "@/components/ContextProvider/BakabaseContextProvider";
import PostParserPage from "@/pages/post-parser";
import {
  useAppOptionsStore,
  useSoulPlusOptionsStore,
  useThirdPartyOptionsStore,
} from "@/stores/options";

if (!import.meta.env.DEV)
  throw new Error("This entry is available only in the development preview.");
const options = getPreviewOptions();

useAppOptionsStore.getState().update(options.app);
useThirdPartyOptionsStore.getState().update(options.thirdParty);
useSoulPlusOptionsStore.getState().update(options.soulPlus);
void i18n.changeLanguage("zh-CN");
resetPreview();
const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

function Preview() {
  const [message, setMessage] = useState("全部内容均为示例，原帖、账号、下载与磁盘操作均已模拟。");
  const [scenario, setScenario] = useState<PreviewScenario>("all");

  useEffect(() => {
    const handle = (event: Event) => setMessage((event as CustomEvent<string>).detail);

    window.addEventListener("post-parser-preview-action", handle);

    return () => window.removeEventListener("post-parser-preview-action", handle);
  }, []);

  return (
    <div className="flex h-full min-w-0 flex-col">
      <aside
        aria-label="预览控制"
        className="flex shrink-0 flex-wrap items-center gap-x-4 gap-y-2 border-b border-default-200 bg-default-50 px-5 py-2 text-xs text-default-500"
      >
        <span className="font-medium text-default-700">交互预览</span>
        <span className="min-w-48 flex-1" role="status">
          {message}
        </span>
        <label className="flex items-center gap-2">
          示例状态
          <select
            className="rounded-md border border-default-200 bg-background px-2 py-1 text-foreground"
            value={scenario}
            onChange={(event) => {
              const value = event.target.value as PreviewScenario;

              setScenario(value);
              setPreviewScenario(value);
            }}
          >
            <option value="all">全部状态</option>
            <option value="waiting">等待处理</option>
            <option value="complete">已完成 / 多资源</option>
            <option value="failure">失败与重试</option>
            <option value="empty">空列表</option>
          </select>
        </label>
        <button
          className="rounded-md border border-default-200 bg-background px-2.5 py-1 text-foreground hover:bg-default-100"
          onClick={resetPreview}
        >
          重置示例
        </button>
      </aside>
      <main className="min-h-0 min-w-0 flex-1 overflow-auto p-5 sm:p-6">
        <PostParserPage />
      </main>
    </div>
  );
}
ReactDOM.createRoot(document.getElementById("root")!).render(
  <HashRouter>
    <QueryClientProvider client={queryClient}>
      <BakabaseContextProvider>
        <Preview />
      </BakabaseContextProvider>
    </QueryClientProvider>
  </HashRouter>,
);
