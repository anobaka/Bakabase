"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineFileZip,
  AiOutlineThunderbolt,
  AiOutlineFile,
  AiOutlineSync,
} from "react-icons/ai";

const FileProcessorSlide = () => {
  const { t } = useTranslation();

  const features = [
    {
      icon: <AiOutlineFileZip className="text-xl" />,
      text: t("onboarding.fileProcessor.point1"),
    },
    {
      icon: <AiOutlineSync className="text-xl" />,
      text: t("onboarding.fileProcessor.point2"),
    },
    {
      icon: <AiOutlineFile className="text-xl" />,
      text: t("onboarding.fileProcessor.point3"),
    },
  ];

  return (
    <div className="flex flex-col items-center p-8 min-h-[400px]">
      <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-primary/10">
        <AiOutlineThunderbolt className="text-5xl text-primary" />
      </div>

      <h2 className="text-2xl font-bold mb-2">
        {t("onboarding.fileProcessor.title")}
      </h2>

      <p className="text-default-500 text-center mb-6 max-w-md">
        {t("onboarding.fileProcessor.subtitle")}
      </p>

      <ul className="space-y-4 max-w-md w-full">
        {features.map((feature, index) => (
          <li
            key={index}
            className="flex items-start gap-3 p-3 rounded-lg bg-default-100"
          >
            <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary">
              {feature.icon}
            </div>
            <span className="text-default-700 pt-1">{feature.text}</span>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default FileProcessorSlide;
