"use client";

import type { StringValueRendererBaseProps } from "./hooks/useStringValueState";

import SingleLineTextRenderer from "./SingleLineTextRenderer";
import MultilineTextRenderer from "./MultilineTextRenderer";

type StringValueRendererProps = StringValueRendererBaseProps & {
  multiline?: boolean;
};

const StringValueRenderer = (props: StringValueRendererProps) => {
  const { multiline, ...baseProps } = props;

  if (multiline) {
    return <MultilineTextRenderer {...baseProps} />;
  }

  return <SingleLineTextRenderer {...baseProps} />;
};

StringValueRenderer.displayName = "StringValueRenderer";

export default StringValueRenderer;
