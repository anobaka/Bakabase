"use client";

import type { Entry } from "@/core/models/FileExplorer/Entry";

import {
  LoadingOutlined,
  RightOutlined,
  WarningOutlined,
} from "@ant-design/icons";
import React, { useCallback } from "react";

import { EntryError, EntryStatus } from "@/core/models/FileExplorer/Entry";
import OperationButton from "../OperationButton";
import { Chip, Tooltip } from "@/components/bakaui";

type Props = {
  loading: boolean;
  entry: Entry;
  expandable?: boolean;
};
const LeftIcon = ({ loading, entry, expandable }: Props) => {
  const renderInner = useCallback(() => {
    if (loading) {
      return <LoadingOutlined className="text-base animate-spin" />;
    } else {
      switch (entry.status) {
        case EntryStatus.Default:
          if (expandable && entry.expandable) {
            return (
              <OperationButton
                isIconOnly
                onClick={() => {
                  if (entry.expanded) {
                    entry.ref?.collapse();
                  } else {
                    entry.ref?.expand();
                  }
                }}
              >
                <RightOutlined
                  className={`text-xs transition-transform duration-200 ease-out ${
                    entry.expanded ? "rotate-90" : "rotate-0"
                  }`}
                />
              </OperationButton>
            );
          }
          break;
        case EntryStatus.Loading:
          break;
        case EntryStatus.Error:
          return (
            <Tooltip
              content={
                <ul className="text-danger">
                  {(Object.keys(entry.errors) as unknown as EntryError[]).map((e) => {
                    return <li key={e}>{entry.errors[e]}</li>;
                  })}
                </ul>
              }
            >
              <Chip color={"danger"} size={"sm"} variant={"light"}>
                <WarningOutlined className="text-base" />
              </Chip>
            </Tooltip>
          );
      }
    }

    return null;
  }, [entry, loading, expandable]);

  return (
    <div className="w-5 h-5 min-w-5 max-w-5 min-h-5 max-h-5 flex items-center justify-center mr-1.5">
      {renderInner()}
    </div>
  );
};

LeftIcon.displayName = "LeftIcon";

export default LeftIcon;
