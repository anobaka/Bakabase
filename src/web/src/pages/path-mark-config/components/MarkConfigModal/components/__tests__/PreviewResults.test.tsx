import type { ReactNode } from "react";

import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import PreviewResults from "../PreviewResults";

import { PathMarkApplyScope, PathMarkType } from "@/sdk/constants";
import translations from "@/locales/en/pages/pathMarkConfig.json";

vi.mock("@/components/bakaui", () => ({
  Chip: ({ children }: { children: ReactNode }) => <span data-testid="value-chip">{children}</span>,
  Spinner: () => <span>Loading</span>,
}));

const props = {
  loading: false,
  error: null,
  results: [
    {
      path: "/media/精品/七彩直播/a.mp4",
      valueSourcePath: "/media/精品/七彩直播",
      propertyValue: "七彩直播",
    },
  ],
  applyScope: PathMarkApplyScope.MatchedAndSubdirectories,
  t: (key: string) => (translations as Record<string, string>)[key] ?? key,
};

afterEach(cleanup);

describe("PreviewResults", () => {
  it.each([PathMarkType.Property, PathMarkType.MediaLibrary])(
    "shows the example path, exact value source and final value separately (type %s)",
    (markType) => {
      render(<PreviewResults {...props} markType={markType} />);
      expect(screen.getByText("Example path")).toBeInTheDocument();
      expect(screen.getByText(props.results[0].path)).toBeInTheDocument();
      expect(screen.getByText(props.results[0].valueSourcePath)).toBeInTheDocument();
      expect(screen.getByText("七彩直播")).toBeInTheDocument();
      expect(screen.queryByText("and all subdirectories")).not.toBeInTheDocument();
    },
  );

  it("shows a missing extraction result explicitly", () => {
    render(
      <PreviewResults
        {...props}
        markType={PathMarkType.MediaLibrary}
        results={[{ path: "/media/精品/七彩直播", propertyValue: null }]}
      />,
    );
    expect(screen.getByText("No value extracted")).toBeInTheDocument();
    expect(screen.queryByText("Value source path")).not.toBeInTheDocument();
  });

  it.each([null, undefined, "", "   "])(
    "does not render an empty colored value for %s",
    (value) => {
      render(
        <PreviewResults
          {...props}
          markType={PathMarkType.Property}
          results={[{ path: "/media/精品", propertyValue: value }]}
        />,
      );
      expect(screen.getByText("No value extracted")).toBeInTheDocument();
      expect(screen.queryByTestId("value-chip")).not.toBeInTheDocument();
    },
  );

  it("shows setup guidance instead of stale values while a property is unconfigured", () => {
    render(
      <PreviewResults
        {...props}
        configurationHint="pathMarkConfig.preview.configureProperty"
        markType={PathMarkType.Property}
      />,
    );
    expect(screen.getByRole("status")).toHaveTextContent(
      "Select a target property and configure how to obtain its value first.",
    );
    expect(screen.queryByTestId("value-chip")).not.toBeInTheDocument();
    expect(screen.queryByText(props.results[0].propertyValue)).not.toBeInTheDocument();
  });

  it("keeps the descendant scope label for resource matching", () => {
    render(<PreviewResults {...props} markType={PathMarkType.Resource} />);
    expect(screen.getByText("Matched path")).toBeInTheDocument();
    expect(screen.getByText("and all subdirectories")).toBeInTheDocument();
  });
});
