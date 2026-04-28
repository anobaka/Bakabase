import React from "react";

import {
  ResourceDetailLayoutEditor,
  renderSamplePlaceholder,
} from "@/components/ResourceDetailLayoutEditor";

export default function ResourceDetailLayoutEditorTest() {
  return (
    <div className="p-3">
      <div className="mb-3 text-sm text-default-600">
        Drag a section by its handle to move it on the grid; drag its edges to resize. Hover over
        an empty cell to add a hidden section there. The layout is rendered as a column-aware
        waterfall at runtime — designer row heights are approximate, real heights come from
        content.
      </div>
      <ResourceDetailLayoutEditor
        configDescription="Sections on the canvas show placeholder content for layout preview only."
        renderSection={renderSamplePlaceholder}
      />
    </div>
  );
}
