"use client";

import React from "react";
import { FolderOpenOutlined } from "@ant-design/icons";

import { Button, Snippet } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface Props {
  path?: string;
  /** Small muted line under the path, explaining what lives there. */
  description?: React.ReactNode;
  size?: "sm" | "md" | "lg";
  className?: string;
}

/**
 * A filesystem path the user can copy or reveal in their file manager.
 *
 * Extracted from the app-info settings rows so anywhere that needs to point the
 * user at a directory — the log page, settings — shows the same affordances
 * instead of re-inventing a snippet plus an open button.
 */
const FilePathValue: React.FC<Props> = ({ path, description, size = "sm", className }) => {
  if (!path) {
    return null;
  }

  return (
    <div className={`flex flex-col gap-1 ${className ?? ""}`}>
      <div className="flex items-center gap-1">
        <Snippet hideSymbol size={size} variant="bordered">
          {path}
        </Snippet>
        <Button
          isIconOnly
          color="primary"
          size={size}
          variant="light"
          onPress={() => BApi.tool.openFileOrDirectory({ path })}
        >
          <FolderOpenOutlined className="text-base" />
        </Button>
      </div>
      {description && <span className="text-xs text-foreground-400">{description}</span>}
    </div>
  );
};

FilePathValue.displayName = "FilePathValue";

export default FilePathValue;
