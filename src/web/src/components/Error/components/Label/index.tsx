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
      <span>{error ?? t<string>("We have encountered some problems.")}</span>
      <span
        className={"cursor-pointer"}
        role="button"
        style={{ color: "var(--bakaui-primary)" }}
        tabIndex={0}
        onClick={() => createPortal(ErrorModal, {})}
        onKeyDown={(e) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            createPortal(ErrorModal, {});
          }
        }}
      >
        {t<string>("how should I handle this problem?")}
      </span>
    </div>
  );
};

Label.displayName = "Label";

export default Label;
