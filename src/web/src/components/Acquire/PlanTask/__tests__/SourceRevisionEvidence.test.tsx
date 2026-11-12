import type { ReactNode } from "react";

import { render, screen } from "@testing-library/react";
import { createInstance } from "i18next";
import { expect, it, vi } from "vitest";

import SourceRevisionEvidence, { sourceRevisionEvidenceLabel } from "../SourceRevisionEvidence";

import cn from "@/locales/cn/pages/acquisition.json";
import en from "@/locales/en/pages/acquisition.json";

const tooltip = vi.hoisted(() => vi.fn());

vi.mock("@/components/bakaui", () => ({
  Button: ({ children }: { children: ReactNode }) => <button>{children}</button>,
  Tooltip: (props: { children: ReactNode; content: ReactNode }) => {
    tooltip(props);

    return <>{props.children}</>;
  },
}));

async function translator(language: "cn" | "en") {
  const i18n = createInstance();

  await i18n.init({
    lng: language,
    resources: { cn: { translation: cn }, en: { translation: en } },
    interpolation: { escapeValue: false },
  });

  return i18n;
}

it.each(["cn", "en"] as const)(
  "translates known evidence in %s without changing identifiers",
  async (language) => {
    const { t } = await translator(language);

    expect(sourceRevisionEvidenceLabel("savedPlanMirror", t)).not.toContain("savedPlanMirror");
    const post = sourceRevisionEvidenceLabel("postParser:137:6:group:main-a", t);

    expect(post).toContain("#137");
    expect(post).toContain("6");
    expect(post).toContain("main-a");
    expect(post).not.toContain("postParser:");
    expect(sourceRevisionEvidenceLabel("postParser:invalid:6:group:main-a", t)).toBe(
      t("acquisition.sourceRevision.evidence.other"),
    );
  },
);

it("keeps unrecognized evidence available in a delayed tooltip, outside the main content", () => {
  const evidence = "futureProducer:opaque-evidence-identifier";

  render(<SourceRevisionEvidence evidence={evidence} />);
  expect(screen.getByText("acquisition.sourceRevision.evidence.other")).toBeVisible();
  expect(document.body).not.toHaveTextContent(evidence);
  const props = tooltip.mock.lastCall![0];

  expect(props).toMatchObject({ delay: 1000, placement: "bottom" });
  render(props.content);
  expect(screen.getByText(evidence)).toBeVisible();
});
