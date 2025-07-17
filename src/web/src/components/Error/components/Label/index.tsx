"use client";

import { useTranslation } from "react-i18next";
import { useEffect } from "react";

import ErrorModal from "../Modal";

interface IProps {
  error?: string;
}

export default ({ error }: IProps) => {
  const { t } = useTranslation();

  useEffect(() => {}, []);

  return (
    <div className={"flex gap-2 items-center"}>
      <span>{error ?? t<string>("We have encountered some problems.")}</span>
      <span
        className={"cursor-pointer"}
        style={{ color: "var(--bakaui-primary)" }}
        onClick={() => ErrorModal.show({})}
      >
        {t<string>("how should I handle this problem?")}
      </span>
    </div>
  );
};
