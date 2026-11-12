import { HeroUIProvider } from "@heroui/react";
import { cleanup, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import AttentionReasonFilter from "../components/AttentionReasonFilter";
import cn from "@/locales/cn/pages/postParser.json";

vi.mock("@/components/bakaui", async () => ({
  ...(await import("@heroui/react")),
  Button: (await import("@/components/bakaui/components/Button")).Button,
}));
vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, unknown> = {}) =>
      ((cn as Record<string, string>)[key] ?? key).replace(/\{\{(\w+)\}\}/g, (_, name) =>
        String(values[name] ?? ""),
      ),
  }),
}));
afterEach(cleanup);

describe("projected attention reason buttons", () => {
  it("renders translated overlapping counts without an invented summed total and selects codes", async () => {
    const change = vi.fn();
    const user = userEvent.setup();
    render(
      <HeroUIProvider disableAnimation>
        <AttentionReasonFilter
          counts={{ priceUnknown: 2, purchaseRequired: 3 }}
          onChange={change}
        />
      </HeroUIProvider>,
    );
    expect(screen.queryByText(cn["postParser.filters.reasonHint"])).toBeNull();
    await user.tab();
    expect(await screen.findByRole("tooltip")).toHaveTextContent(
      cn["postParser.filters.reasonHint"],
    );
    expect(screen.getByRole("group", { name: cn["postParser.filters.reason"] })).toBeVisible();
    expect(screen.getByRole("button", { name: /需要购买解锁\s*3/ })).toBeVisible();
    expect(
      screen.getByRole("button", { name: cn["postParser.filters.anyReason"] }),
    ).toHaveAttribute("aria-pressed", "true");
    expect(screen.queryByRole("listbox")).toBeNull();
    await user.click(screen.getByRole("button", { name: /购买价格未知\s*2/ }));
    expect(change).toHaveBeenCalledWith("priceUnknown");
    expect(screen.queryByText("5")).toBeNull();
  });
  it("keeps a selected zero-count future code visible and allows clearing it", async () => {
    const change = vi.fn();
    const user = userEvent.setup();
    render(
      <HeroUIProvider disableAnimation>
        <AttentionReasonFilter counts={{}} value="futureCode" onChange={change} />
      </HeroUIProvider>,
    );
    const selected = screen.getByRole("button", { name: /其他待处理原因（futureCode）\s*0/ });
    expect(selected).toBeVisible();
    expect(selected).toHaveAttribute("aria-pressed", "true");
    expect(
      screen.getByRole("button", { name: cn["postParser.filters.anyReason"] }),
    ).toHaveAttribute("aria-pressed", "false");
    await user.click(selected);
    expect(change).toHaveBeenLastCalledWith("futureCode");
    await user.click(screen.getByRole("button", { name: cn["postParser.filters.anyReason"] }));
    expect(change).toHaveBeenCalledWith(undefined);
  });
  it("keeps unavailable counts unknown and disables input until metadata arrives", () => {
    render(
      <HeroUIProvider disableAnimation>
        <AttentionReasonFilter onChange={vi.fn()} />
      </HeroUIProvider>,
    );
    expect(screen.getByRole("button", { name: cn["postParser.filters.anyReason"] })).toBeDisabled();
  });
});
