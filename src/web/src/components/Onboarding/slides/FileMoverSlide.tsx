"use client";

import { useTranslation } from "react-i18next";
import {
  AiOutlineSwap,
  AiOutlineFolderAdd,
  AiOutlineEdit,
  AiOutlineOrderedList,
} from "react-icons/ai";

const FileMoverSlide = () => {
  const { t } = useTranslation();

  const features = [
    {
      icon: <AiOutlineFolderAdd className="text-xl" />,
      text: t("onboarding.fileMover.point1"),
    },
    {
      icon: <AiOutlineEdit className="text-xl" />,
      text: t("onboarding.fileMover.point2"),
    },
    {
      icon: <AiOutlineOrderedList className="text-xl" />,
      text: t("onboarding.fileMover.point3"),
    },
  ];

  return (
    <div className="flex flex-col items-center p-8 min-h-[400px]">
      <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-cyan-500/10">
        <AiOutlineSwap className="text-5xl text-cyan-500" />
      </div>

      <h2 className="text-2xl font-bold mb-2">
        {t("onboarding.fileMover.title")}
      </h2>

      <p className="text-default-500 text-center mb-6 max-w-md">
        {t("onboarding.fileMover.subtitle")}
      </p>

      <ul className="space-y-4 max-w-md w-full">
        {features.map((feature, index) => (
          <li
            key={index}
            className="flex items-start gap-3 p-3 rounded-lg bg-default-100"
          >
            <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-cyan-500/10 text-cyan-500">
              {feature.icon}
            </div>
            <span className="text-default-700 pt-1">{feature.text}</span>
          </li>
        ))}
      </ul>
    </div>
  );
};

export default FileMoverSlide;
