"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineSetting } from "react-icons/ai";

const CompletionSlide = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col items-center justify-center p-8 min-h-[400px]">
      <div className="w-24 h-24 mb-6 flex items-center justify-center rounded-full bg-success/10">
        <AiOutlineCheckCircle className="text-6xl text-success" />
      </div>

      <h2 className="text-3xl font-bold mb-4">
        {t("onboarding.completion.title")}
      </h2>

      <p className="text-lg text-default-500 text-center max-w-md mb-6">
        {t("onboarding.completion.description")}
      </p>

      <div className="flex items-center gap-2 text-sm text-default-400">
        <AiOutlineSetting className="text-base" />
        <span>{t("onboarding.completion.hint")}</span>
      </div>
    </div>
  );
};

export default CompletionSlide;
