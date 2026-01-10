"use client";

import type { CarouselRef } from "antd/es/carousel";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineFolder,
  AiOutlineFileAdd,
  AiOutlineTags,
  AiOutlineAppstore,
  AiOutlineSync,
} from "react-icons/ai";
import { MdVideoLibrary } from "react-icons/md";

import Modal from "@/components/bakaui/components/Modal";
import Carousel from "@/components/bakaui/components/Carousel";
import { Button } from "@/components/bakaui";

interface PathMarkGuideModalProps {
  visible: boolean;
  onComplete: () => void;
}

const TOTAL_SLIDES = 4;

const PathMarkGuideModal = ({ visible, onComplete }: PathMarkGuideModalProps) => {
  const { t } = useTranslation();
  const carouselRef = useRef<CarouselRef>(null);
  const [currentSlide, setCurrentSlide] = useState(0);

  const isFirst = currentSlide === 0;
  const isLast = currentSlide === TOTAL_SLIDES - 1;

  const handlePrev = () => {
    carouselRef.current?.prev();
  };

  const handleNext = () => {
    if (isLast) {
      onComplete();
    } else {
      carouselRef.current?.next();
    }
  };

  const handleSkip = () => {
    onComplete();
  };

  const handleSlideChange = (current: number) => {
    setCurrentSlide(current);
  };

  return (
    <Modal
      hideCloseButton
      classNames={{
        base: "max-w-[900px]",
      }}
      footer={false}
      isDismissable={false}
      isKeyboardDismissDisabled={true}
      size="4xl"
      visible={visible}
    >
      <div className="flex flex-col">
        <Carousel ref={carouselRef} afterChange={handleSlideChange} dots={false} infinite={false}>
          {/* Slide 1: Welcome / Introduction */}
          <div>
            <div className="flex flex-col items-center justify-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-primary/10">
                <AiOutlineTags className="text-4xl text-primary" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("pathMarkGuide.welcome.title")}
              </h2>

              <p className="text-sm text-default-500 text-center max-w-lg">
                {t("pathMarkGuide.welcome.description")}
              </p>
            </div>
          </div>

          {/* Slide 2: Three Mark Types */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-secondary/10">
                <AiOutlineAppstore className="text-4xl text-secondary" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("pathMarkGuide.markTypes.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("pathMarkGuide.markTypes.subtitle")}
              </p>

              <ul className="space-y-2 max-w-lg w-full">
                {/* Resource Mark - highlighted as core */}
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-primary/5 border border-primary/20">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary">
                    <AiOutlineFileAdd className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-primary text-sm">
                      {t("pathMarkGuide.markTypes.resource")}
                    </span>
                    <p className="text-sm text-default-500 truncate">
                      {t("pathMarkGuide.markTypes.resourceDesc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary">
                    <AiOutlineTags className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("pathMarkGuide.markTypes.property")}
                    </span>
                    <p className="text-sm text-default-500 truncate">
                      {t("pathMarkGuide.markTypes.propertyDesc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary">
                    <MdVideoLibrary className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("pathMarkGuide.markTypes.mediaLibrary")}
                    </span>
                    <p className="text-sm text-default-500 truncate">
                      {t("pathMarkGuide.markTypes.mediaLibraryDesc")}
                    </p>
                  </div>
                </li>
              </ul>

              {/* Important note about Resource Mark */}
              <div className="px-3 py-2 rounded-lg bg-warning/5 border border-warning/20 max-w-lg w-full mt-3">
                <p className="text-default-700 text-sm">
                  {t("pathMarkGuide.markTypes.resourceImportant")}
                </p>
              </div>
            </div>
          </div>

          {/* Slide 3: How to Configure */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-warning/10">
                <AiOutlineFolder className="text-4xl text-warning" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("pathMarkGuide.configure.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("pathMarkGuide.configure.subtitle")}
              </p>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning font-bold text-sm">
                    1
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("pathMarkGuide.configure.step1")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning font-bold text-sm">
                    2
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("pathMarkGuide.configure.step2")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning font-bold text-sm">
                    3
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("pathMarkGuide.configure.step3")}
                  </span>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 4: Sync & Management */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-success/10">
                <AiOutlineSync className="text-4xl text-success" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("pathMarkGuide.sync.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("pathMarkGuide.sync.subtitle")}
              </p>

              <div className="px-3 py-2 rounded-lg bg-warning/5 border border-warning/20 max-w-lg w-full mb-3">
                <p className="text-default-700 text-sm">
                  {t("pathMarkGuide.sync.explanation")}
                </p>
              </div>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    1
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("pathMarkGuide.sync.point1")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    2
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("pathMarkGuide.sync.point2")}
                  </span>
                </li>
              </ul>
            </div>
          </div>
        </Carousel>

        {/* Progress dots */}
        <div className="flex justify-center gap-2 py-3">
          {Array.from({ length: TOTAL_SLIDES }).map((_, index) => (
            <div
              key={index}
              className={`w-2 h-2 rounded-full transition-all ${
                index === currentSlide ? "bg-primary w-6" : "bg-default-300"
              }`}
            />
          ))}
        </div>

        {/* Navigation buttons */}
        <div className="flex justify-between items-center px-6 pb-5">
          <Button variant="light" onPress={handleSkip}>
            {t("pathMarkGuide.skip")}
          </Button>
          <div className="flex gap-2">
            <Button isDisabled={isFirst} variant="flat" onPress={handlePrev}>
              {t("pathMarkGuide.previous")}
            </Button>
            <Button color="primary" onPress={handleNext}>
              {isLast ? t("pathMarkGuide.getStarted") : t("pathMarkGuide.next")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default PathMarkGuideModal;
