import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";

import { getHelpTopic, helpTopics } from "../topics";
import CollectionMemoTopic from "../topics/collectionMemo";

import cnHelp from "@/locales/cn/components/helpCollectionMemo.json";
import enHelp from "@/locales/en/components/helpCollectionMemo.json";

afterEach(cleanup);

describe("collection memo help", () => {
  it("registers the guide and covers range inheritance and timeline interactions", () => {
    const topic = helpTopics.find((item) => item.id === "collectionMemo");

    expect(getHelpTopic("collectionMemo")).toBe(topic);
    expect(topic?.Content).toBe(CollectionMemoTopic);
    render(<CollectionMemoTopic />);
    for (const key of [
      "step.target.desc",
      "step.range.desc",
      "step.review.desc",
      "settings.start.desc",
      "settings.direction.desc",
      "timeline.drag",
      "timeline.keyboard",
      "timeline.fill",
      "timeline.retry",
      "dates",
      "browsingIntegration",
    ]) {
      expect(screen.getByText(`helpCenter.collectionMemo.${key}`)).toBeInTheDocument();
    }
  });

  it("provides matching Chinese and English translations for all guide content", () => {
    expect(Object.keys(cnHelp).sort()).toEqual(Object.keys(enHelp).sort());
    for (const value of [...Object.values(cnHelp), ...Object.values(enHelp)]) {
      expect(value.trim()).not.toBe("");
    }
    expect(cnHelp["helpCenter.collectionMemo.timeline.drag"]).toContain("Esc");
    expect(enHelp["helpCenter.collectionMemo.timeline.drag"]).toContain("Esc");
    expect(cnHelp["helpCenter.collectionMemo.timeline.keyboard"]).toContain("Home / End");
    expect(enHelp["helpCenter.collectionMemo.timeline.keyboard"]).toContain("Home / End");
  });
});
