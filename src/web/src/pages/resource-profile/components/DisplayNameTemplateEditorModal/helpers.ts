import type { ResourceDisplayNameTemplateSegment } from "@/core/models/Category/ResourceDisplayNameTemplate.ts";
import type { Wrapper } from "@/core/models/Text/Wrapper.ts";

import { ResourceDisplayNameTemplateSegmentType } from "@/core/models/Category/ResourceDisplayNameTemplate.ts";

const VariableWrapper: Wrapper = {
  left: "{",
  right: "}",
};

export const extractResourceDisplayNameTemplate = (
  template: string,
  variables: string[],
  wrappers: Wrapper[],
): ResourceDisplayNameTemplateSegment[] => {
  const wrappedVariables = variables.map(
    (v) => `${VariableWrapper.left}${v}${VariableWrapper.right}`,
  );
  const segments: ResourceDisplayNameTemplateSegment[] = [];

  for (let i = 0; i < template.length; i++) {
    const c = template[i];
    let matched = false;

    if (c == "{") {
      for (const wv of wrappedVariables) {
        if (template.startsWith(wv, i)) {
          segments.push({
            text: wv,
            type: ResourceDisplayNameTemplateSegmentType.Property,
          });
          i += wv.length - 1;
          matched = true;
          break;
        }
      }
    }
    if (!matched) {
      if (wrappers.some((w) => w.left == c || w.right == c)) {
        segments.push({
          text: c,
          type: ResourceDisplayNameTemplateSegmentType.Wrapper,
        });
      } else {
        const prevPart = segments[segments.length - 1];

        if (prevPart?.type == ResourceDisplayNameTemplateSegmentType.Text) {
          prevPart.text += c;
        } else {
          segments.push({
            text: c,
            type: ResourceDisplayNameTemplateSegmentType.Text,
          });
        }
      }
    }
  }

  return segments;
};
