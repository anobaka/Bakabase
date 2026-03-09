"use client";

import { useTranslation } from "react-i18next";
import { useEffect } from "react";

import ErrorModal from "../Modal";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  error?: string;
}
const Label = ({ error }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  useEffect(() => {}, []);

  return (
    <div className={"flex gap-2 items-center"}>
      <span>{error ?? t<string>("error.label.encounteredProblems")}</span>
      <span
        className={"cursor-pointer"}
        style={{ color: "var(--bakaui-primary)" }}
        onClick={() => createPortal(ErrorModal, {})}
      >
        {t<string>("error.label.howToHandle")}
      </span>
    </div>
  );
};

Label.displayName = "Label";

export default Label;
