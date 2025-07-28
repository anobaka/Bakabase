"use client";

import React from "react";

import { generateTrees } from "../data/tree";

import { Button, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
const MultilevelValueEditorPage = () => {
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      color={"primary"}
      size={"sm"}
      onClick={() => {
        const trees = generateTrees();

        createPortal(Modal, {
          defaultVisible: true,
          children: renderTreeNodes(trees),
          size: "xl",
        });
      }}
    >
      Open multilevel value editor
    </Button>
  );
};

MultilevelValueEditorPage.displayName = "MultilevelValueEditorPage";

export default MultilevelValueEditorPage;
