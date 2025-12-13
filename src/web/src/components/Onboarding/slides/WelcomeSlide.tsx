"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineAppstore } from "react-icons/ai";

const WelcomeSlide = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col items-center justify-center p-8 min-h-[400px]">
      <div className="w-24 h-24 mb-6 flex items-center justify-center rounded-2xl bg-primary/10">
        <AiOutlineAppstore className="text-6xl text-primary" />
      </div>

      <h1 className="text-3xl font-bold mb-4">
        {t("onboarding.welcome.title")}
      </h1>

      <p className="text-lg text-default-500 text-center max-w-md">
        {t("onboarding.welcome.description")}
      </p>
    </div>
  );
};

export default WelcomeSlide;
