"use client";

import React from "react";
import { useTranslation } from "react-i18next";

import FileSystemEntryChangeExampleItem from "./FileSystemEntryChangeExampleItem";

type Props = {
  indent?: 0 | 1 | 2;
  parent?: string;
};
const FileSystemEntryChangeExampleMiscellaneousItem = ({
  indent,
  parent,
}: Props) => {
  const { t } = useTranslation();

  return (
    <FileSystemEntryChangeExampleItem
      className={"opacity-60"}
      layer={indent}
      text={
        parent
          ? `${t<string>("Other files in {{parent}}", { parent })}...`
          : `${t<string>("Other files")}...`
      }
      type={"others"}
    />
  );
};

FileSystemEntryChangeExampleMiscellaneousItem.displayName =
  "FileSystemEntryChangeExampleMiscellaneousItem";

export default FileSystemEntryChangeExampleMiscellaneousItem;
