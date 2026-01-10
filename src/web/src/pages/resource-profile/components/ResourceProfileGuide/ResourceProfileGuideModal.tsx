"use client";

import type { CarouselRef } from "antd/es/carousel";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineSetting,
  AiOutlineSearch,
  AiOutlineEdit,
  AiOutlineThunderbolt,
  AiOutlinePlayCircle,
  AiOutlineAppstore,
  AiOutlineOrderedList,
  AiOutlineLink,
  AiOutlinePushpin,
} from "react-icons/ai";
import { BsController } from "react-icons/bs";

import Modal from "@/components/bakaui/components/Modal";
import Carousel from "@/components/bakaui/components/Carousel";
import { Button } from "@/components/bakaui";

interface ResourceProfileGuideModalProps {
  visible: boolean;
  onComplete: () => void;
}

const TOTAL_SLIDES = 5;

const ResourceProfileGuideModal = ({ visible, onComplete }: ResourceProfileGuideModalProps) => {
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
        base: "max-w-[800px]",
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
            <div className="flex flex-col items-center justify-center p-8 min-h-[400px]">
              <div className="w-24 h-24 mb-6 flex items-center justify-center rounded-2xl bg-primary/10">
                <AiOutlineSetting className="text-6xl text-primary" />
              </div>

              <h1 className="text-3xl font-bold mb-4 text-center">
                {t("resourceProfileGuide.welcome.title")}
              </h1>

              <p className="text-lg text-default-500 text-center max-w-lg">
                {t("resourceProfileGuide.welcome.description")}
              </p>
            </div>
          </div>

          {/* Slide 2: Search Criteria & Priority */}
          <div>
            <div className="flex flex-col items-center p-8 min-h-[400px]">
              <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-secondary/10">
                <AiOutlineSearch className="text-5xl text-secondary" />
              </div>

              <h2 className="text-2xl font-bold mb-2 text-center">
                {t("resourceProfileGuide.matching.title")}
              </h2>

              <p className="text-default-500 text-center mb-6 max-w-md">
                {t("resourceProfileGuide.matching.subtitle")}
              </p>

              <ul className="space-y-4 max-w-md w-full">
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary">
                    <AiOutlineSearch className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.matching.point1")}
                  </span>
                </li>
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary">
                    <AiOutlineOrderedList className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.matching.point2")}
                  </span>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 3: Display & Enhancers */}
          <div>
            <div className="flex flex-col items-center p-8 min-h-[400px]">
              <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-warning/10">
                <AiOutlineEdit className="text-5xl text-warning" />
              </div>

              <h2 className="text-2xl font-bold mb-2 text-center">
                {t("resourceProfileGuide.display.title")}
              </h2>

              <p className="text-default-500 text-center mb-6 max-w-md">
                {t("resourceProfileGuide.display.subtitle")}
              </p>

              <ul className="space-y-4 max-w-md w-full">
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning">
                    <AiOutlineEdit className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.display.point1")}
                  </span>
                </li>
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning">
                    <AiOutlineThunderbolt className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.display.point2")}
                  </span>
                </li>
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning">
                    <AiOutlineAppstore className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.display.point3")}
                  </span>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 4: Playable Files & Players */}
          <div>
            <div className="flex flex-col items-center p-8 min-h-[400px]">
              <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-success/10">
                <AiOutlinePlayCircle className="text-5xl text-success" />
              </div>

              <h2 className="text-2xl font-bold mb-2 text-center">
                {t("resourceProfileGuide.playback.title")}
              </h2>

              <p className="text-default-500 text-center mb-6 max-w-md">
                {t("resourceProfileGuide.playback.subtitle")}
              </p>

              <ul className="space-y-4 max-w-md w-full">
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success">
                    <AiOutlinePlayCircle className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.playback.point1")}
                  </span>
                </li>
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success">
                    <BsController className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.playback.point2")}
                  </span>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 5: Property Binding */}
          <div>
            <div className="flex flex-col items-center p-8 min-h-[400px]">
              <div className="w-20 h-20 mb-6 flex items-center justify-center rounded-2xl bg-primary/10">
                <AiOutlineLink className="text-5xl text-primary" />
              </div>

              <h2 className="text-2xl font-bold mb-2 text-center">
                {t("resourceProfileGuide.binding.title")}
              </h2>

              <p className="text-default-500 text-center mb-6 max-w-md">
                {t("resourceProfileGuide.binding.subtitle")}
              </p>

              <ul className="space-y-4 max-w-md w-full">
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary">
                    <AiOutlineLink className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.binding.point1")}
                  </span>
                </li>
                <li className="flex items-start gap-3 p-3 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary">
                    <AiOutlinePushpin className="text-xl" />
                  </div>
                  <span className="text-default-700 pt-1">
                    {t("resourceProfileGuide.binding.point2")}
                  </span>
                </li>
              </ul>
            </div>
          </div>
        </Carousel>

        {/* Progress dots */}
        <div className="flex justify-center gap-2 py-4">
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
        <div className="flex justify-between items-center px-6 pb-6">
          <Button variant="light" onPress={handleSkip}>
            {t("resourceProfileGuide.skip")}
          </Button>
          <div className="flex gap-2">
            <Button isDisabled={isFirst} variant="flat" onPress={handlePrev}>
              {t("resourceProfileGuide.previous")}
            </Button>
            <Button color="primary" onPress={handleNext}>
              {isLast ? t("resourceProfileGuide.getStarted") : t("resourceProfileGuide.next")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default ResourceProfileGuideModal;
