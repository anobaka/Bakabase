"use client";

import type { IPscPropertyMatcherValue } from "@/components/PathSegmentsConfiguration/models/PscPropertyMatcherValue";

import React from "react";
import { useTranslation } from "react-i18next";

import Validate from "./Validate";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  value: IPscPropertyMatcherValue[];
  hasError: boolean;
};
const BottomOperations = ({ value, hasError }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  return (
    <div className="">
      <Validate isDisabled={hasError} value={value} />
    </div>
  );
};

BottomOperations.displayName = "BottomOperations";

export default BottomOperations;
