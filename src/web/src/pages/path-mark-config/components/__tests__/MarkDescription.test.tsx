import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";

import MarkDescription from "../MarkDescription";

import translations from "@/locales/en/pages/pathMarkConfig.json";
import { PathMarkType, PathMatchMode, PropertyValueType } from "@/sdk/constants";

vi.mock("react-i18next", () => ({
  useTranslation: () => ({
    t: (key: string, values: Record<string, string | number> = {}) => {
      const template = (translations as Record<string, string>)[key] ?? key;

      return template.replace(/{{(\w+)}}/g, (_, name: string) => String(values[name] ?? ""));
    },
  }),
}));

afterEach(cleanup);

describe.each([PathMarkType.Property, PathMarkType.MediaLibrary])(
  "MarkDescription (type %s)",
  (type) => {
    const renderDescription = (config: Record<string, unknown>) =>
      render(
        <MarkDescription
          mark={{
            type,
            configJson: JSON.stringify({ valueType: PropertyValueType.Dynamic, ...config }),
          }}
        />,
      );

    it("describes mark-directory extraction when layer matching ignores the value regex", () => {
      renderDescription({
        matchMode: PathMatchMode.Layer,
        layer: 1,
        valueRegex: "(ignored)",
        regexToMediaLibrary: "(ignored)",
      });

      expect(screen.getByText(/mark directory name/)).toBeInTheDocument();
      expect(screen.queryByText(/ignored/)).not.toBeInTheDocument();
    });

    it("describes mark-directory regex extraction even when a value layer is also present", () => {
      renderDescription({
        matchMode: PathMatchMode.Regex,
        regex: ".*",
        valueLayer: 0,
        layerToMediaLibrary: 0,
        valueRegex: "(name)",
        regexToMediaLibrary: "(name)",
      });

      expect(screen.getByText(/mark directory regex\(\(name\)\)/)).toBeInTheDocument();
    });

    it.each([
      { layer: 1, text: "name 1 level(s) below mark" },
      { layer: -1, text: "directory name 1 level(s) above mark" },
    ])("explains value layer $layer relative to the mark", ({ layer, text }) => {
      const { container } = renderDescription({
        matchMode: PathMatchMode.Layer,
        layer: 1,
        valueLayer: layer,
        layerToMediaLibrary: layer,
      });

      expect(container).toHaveTextContent(text);
    });
  },
);
