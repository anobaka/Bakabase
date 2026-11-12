import type { ReactNode } from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import { ParsingRecordContext, ParsingRecordEmptyState } from "../components/ParsingRecordContext";
import { PostParseTarget } from "@/sdk/constants";
import cn from "@/locales/cn/pages/postParser.json";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, options?: Record<string, unknown>) => {
      const text = (cn as Record<string, string>)[key] ?? key;
      return text.replace(/{{(\w+)}}/g, (_, field) => String(options?.[field] ?? ""));
    },
  }),
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress }: { children: ReactNode; onPress: () => void }) => (
    <button onClick={onPress}>{children}</button>
  ),
}));

it("explains retained parsing results without inventing an unavailable execution log or offering a reparse", () => {
  const open = vi.fn();
  const task = {
    id: 50,
    source: 0 as const,
    link: "",
    revision: 3,
    targets: [PostParseTarget.DownloadInfo],
    workflowRunId: 499,
    workflowStatus: null,
    results: { DownloadInfo: { resources: [{ link: "https://example.test/file" }] } },
  };
  render(
    <>
      <ParsingRecordContext task={task} />
      <ParsingRecordEmptyState task={task} onProcessingHistory={open} />
    </>,
  );
  expect(screen.getByText("解析结果已保留，可继续用于获取文件。")).toBeVisible();
  expect(screen.getByText(/#499/)).toHaveTextContent("当前找不到可查看的详细执行记录");
  expect(screen.getByText(/无需为补齐日志重新解析/)).toBeVisible();
  expect(screen.queryByRole("button", { name: /重新解析/ })).toBeNull();
  fireEvent.click(screen.getByRole("button", { name: "查看后续文件处理记录" }));
  expect(open).toHaveBeenCalledTimes(1);
});
