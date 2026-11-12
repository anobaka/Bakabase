import type { ComponentProps, ReactNode } from "react";

import { HeroUIProvider } from "@heroui/react";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import DeleteTasksConfirmationModal from "../components/DeleteTasksConfirmationModal";
import ActualModal from "@/components/bakaui/components/Modal";
import cn from "@/locales/cn/pages/postParser.json";
import en from "@/locales/en/pages/postParser.json";

const api = vi.hoisted(() => ({ confirm: vi.fn(), close: vi.fn(), danger: vi.fn() }));
vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
  Modal: (props: ComponentProps<typeof ActualModal>) => <ActualModal {...props} />,
  toast: { danger: api.danger },
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      ),
  }),
  Trans: ({ children }: { children: ReactNode }) => children,
}));

const show = (props: Partial<ComponentProps<typeof DeleteTasksConfirmationModal>> = {}) => {
  render(
    <HeroUIProvider disableAnimation>
      <DeleteTasksConfirmationModal
        count={52}
        keyword="sample keyword"
        status="failed"
        onConfirm={api.confirm}
        onClose={api.close}
        {...props}
      />
    </HeroUIProvider>,
  );
  return userEvent.setup();
};
beforeEach(() => {
  api.confirm.mockReset().mockResolvedValue(undefined);
  api.close.mockReset();
});
afterEach(cleanup);

describe("post parser delete selection confirmation", () => {
  it("shows the precise cross-page scope and preserves related acquisitions, files and history", async () => {
    show();
    expect(await screen.findByRole("dialog")).toHaveTextContent("删除全部 52 个筛选结果？");
    expect(screen.getByRole("dialog")).toHaveTextContent("sample keyword");
    expect(screen.getByRole("dialog")).toHaveTextContent("失败");
    expect(screen.getByRole("dialog")).toHaveTextContent("其他分页");
    expect(screen.getByRole("dialog")).toHaveTextContent("之后新增的匹配任务不会一并删除");
    expect(screen.getByRole("dialog")).toHaveTextContent("获取任务、下载文件和执行历史会保留");
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("defaults focus and Enter to cancellation instead of deletion", async () => {
    const user = show();
    const cancel = await screen.findByRole("button", { name: "取消，保留任务" });
    await waitFor(() => expect(cancel).toHaveFocus());
    await user.keyboard("{Enter}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.close).toHaveBeenCalledOnce();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("labels an unfiltered selection as all tasks and allows Escape to cancel", async () => {
    const user = show({ keyword: "", status: "all" });
    expect(await screen.findByRole("dialog")).toHaveTextContent("删除全部 52 个帖子解析任务？");
    expect(screen.getByRole("dialog")).toHaveTextContent("不限关键词");
    expect(screen.getByRole("dialog")).toHaveTextContent("全部状态");
    await user.keyboard("{Escape}");
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("requires an explicit dangerous confirmation and blocks repeat submits before React commits", async () => {
    let finish!: () => void;
    api.confirm.mockImplementation(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve;
        }),
    );
    show();
    const confirm = await screen.findByRole("button", { name: "确认删除 52 个任务" });
    act(() => {
      confirm.click();
      confirm.click();
    });
    expect(api.confirm).toHaveBeenCalledOnce();
    expect(confirm).toBeDisabled();
    expect(screen.getByRole("button", { name: "取消，保留任务" })).toBeDisabled();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    await act(async () => finish());
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("keeps failed confirmations open so the user can inspect the error or cancel", async () => {
    api.confirm.mockRejectedValueOnce(new Error("Selection changed. Preview it again."));
    const user = show();
    await user.click(await screen.findByRole("button", { name: "确认删除 52 个任务" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Selection changed. Preview it again.",
    );
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(api.close).not.toHaveBeenCalled();
    await user.click(screen.getByRole("button", { name: "取消，保留任务" }));
    expect(api.confirm).toHaveBeenCalledOnce();
  });

  it("does not confirm an empty selection", async () => {
    show({ count: 0 });
    const confirm = await screen.findByRole("button", { name: "确认删除 0 个任务" });
    expect(confirm).toBeDisabled();
    confirm.click();
    expect(api.confirm).not.toHaveBeenCalled();
  });

  it("provides translated count and preservation text in both supported languages", () => {
    for (const messages of [cn, en]) {
      expect(messages["postParser.deleteBatch.confirm"]).toContain("{{count}}");
      expect(messages["postParser.deleteBatch.scope"]).toContain("{{count}}");
      for (const key of Object.keys(cn).filter((key) => key.startsWith("postParser.deleteBatch.")))
        expect((messages as Record<string, string>)[key]).toBeTruthy();
    }
  });
});
