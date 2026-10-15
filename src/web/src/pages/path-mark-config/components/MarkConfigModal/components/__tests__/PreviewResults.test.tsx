import type { ReactNode } from "react";

import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import PreviewResults from "../PreviewResults";

import { PathMarkApplyScope, PathMarkType } from "@/sdk/constants";

vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: ReactNode }) => <span>{children}</span>,
  Spinner: () => <span>Loading</span>,
}));
vi.mock("@/components/Chips/Terms", () => ({ ResourceTerm: () => <span>Resource</span> }));

const translations: Record<string, string> = {
  "pathMarkConfig.label.previewMatches": "Preview matches",
  "pathMarkConfig.label.previewValueExamples":
    "Path value examples (only applied to recognized resources)",
  "pathMarkConfig.label.andAllSubdirectories": "and all subdirectories",
};
const props = {
  loading: false,
  error: null,
  results: [{ path: "/media/精品/七彩直播", propertyValue: "七彩直播" }],
  applyScope: PathMarkApplyScope.MatchedAndSubdirectories,
  t: (key: string) => translations[key] ?? key,
};

afterEach(cleanup);

describe("PreviewResults", () => {
  it.each([PathMarkType.Property, PathMarkType.MediaLibrary])(
    "labels values as examples without claiming descendants all inherit the displayed value (type %s)",
    (markType) => {
      render(<PreviewResults {...props} markType={markType} />);

      expect(screen.getByText(/Path value examples/)).toBeInTheDocument();
      expect(screen.queryByText("and all subdirectories")).not.toBeInTheDocument();
      expect(screen.getByText("七彩直播")).toBeInTheDocument();
    },
  );

  it("keeps the descendant scope indication for resource matching", () => {
    render(<PreviewResults {...props} markType={PathMarkType.Resource} />);

    expect(screen.getByText("Preview matches:")).toBeInTheDocument();
    expect(screen.getByText("and all subdirectories")).toBeInTheDocument();
  });
});
