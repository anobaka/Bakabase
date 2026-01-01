"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineSetting,
  AiOutlineEdit,
  AiOutlineThunderbolt,
  AiOutlinePlayCircle,
} from "react-icons/ai";

const ResourceProfileSlide = () => {
  const { t } = useTranslation();

  const features = [
    {
      icon: <AiOutlineEdit className="text-xl" />,
      text: t("onboarding.resourceProfile.point1"),
    },
    {
      icon: <AiOutlineThunderbolt className="text-xl" />,
      text: t("onboarding.resourceProfile.point2"),
    },
    {
      icon: <AiOutlinePlayCircle className="text-xl" />,
      text: t("onboarding.resourceProfile.point3"),
    },
  ];

  return (
    <div className="flex flex-col items-center p-8 min-h-[400px]">
      <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-warning/10">
        <AiOutlineSetting className="text-5xl text-warning" />
      </div>

      <h2 className="text-2xl font-bold mb-2">
        {t("onboarding.resourceProfile.title")}
      </h2>

      <p className="text-default-500 text-center mb-6 max-w-md">
        {t("onboarding.resourceProfile.subtitle")}
      </p>

      <ul className="space-y-4 max-w-md w-full">
        {features.map((feature, index) => (
          <li
            key={index}
            className="flex items-start gap-3 p-3 rounded-lg bg-default-100"
          >
            <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning">
              {feature.icon}
            </div>
            <span className="text-default-700 pt-1">{feature.text}</span>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default ResourceProfileSlide;
