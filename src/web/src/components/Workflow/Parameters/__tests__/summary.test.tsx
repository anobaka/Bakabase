import { createInstance } from "i18next";
import { render, screen } from "@testing-library/react";
import { expect, it, vi } from "vitest";
import type { ReactNode } from "react";
import WorkflowParameterSummary from "../WorkflowParameterSummary";
import cn from "@/locales/cn/pages/workflow.json";
import en from "@/locales/en/pages/workflow.json";

const language = vi.hoisted(() => ({ translate: (key: string) => key }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: language.translate }) }));
vi.mock("@/components/bakaui", () => ({
  Accordion: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  AccordionItem: ({ children, title }: { children: ReactNode; title: string }) => <section><h3>{title}</h3>{children}</section>,
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
}));

it.each([{ lng: "cn", messages: cn }, { lng: "en", messages: en }])(
  "translates layout parameters and preserves safe custom values with the actual $lng missing-key handler",
  async ({ lng, messages }) => {
    const i18n = createInstance();
    await i18n.init({ lng, fallbackLng: false, resources: { [lng]: { translation: messages } },
      parseMissingKeyHandler: key => key, interpolation: { escapeValue: false } });
    language.translate = key => i18n.t(key);
    render(<WorkflowParameterSummary sources={[
      { key: "delivery.contentLayout", source: "caller", type: "object", hasValue: true, sensitive: false },
      { key: "download.layout", source: "caller", type: "string", hasValue: true, sensitive: false, displayValue: "currentDirectory" },
      { key: "custom.folder", source: "caller", type: "string", hasValue: true, sensitive: false, displayValue: "/allowed/folder" },
      { key: "secret", source: "caller", type: "string", hasValue: true, sensitive: true, displayValue: "NEVER_SHOW" },
    ]} />);
    expect(screen.getByText(messages["workflow.parameters.names.delivery.contentLayout"])).toBeVisible();
    expect(screen.getByText(messages["workflow.parameters.names.download.layout"])).toBeVisible();
    expect(screen.getByText(messages["workflow.parameters.values.download.layout.currentDirectory"])).toBeVisible();
    expect(screen.getByText("custom.folder")).toBeVisible();
    expect(screen.getByText("/allowed/folder")).toBeVisible();
    expect(document.body.textContent).not.toContain("workflow.parameters.names.");
    expect(document.body.textContent).not.toContain("workflow.parameters.values.");
    expect(document.body.textContent).not.toContain("NEVER_SHOW");
  },
);
