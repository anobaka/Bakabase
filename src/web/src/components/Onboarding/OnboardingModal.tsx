"use client";

import type { CarouselRef } from "antd/es/carousel";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import WelcomeSlide from "./slides/WelcomeSlide";
import PathMarkSlide from "./slides/PathMarkSlide";
import ResourceProfileSlide from "./slides/ResourceProfileSlide";
import ResourceBrowsingSlide from "./slides/ResourceBrowsingSlide";
import FileProcessorSlide from "./slides/FileProcessorSlide";
import DownloaderSlide from "./slides/DownloaderSlide";
import FileMoverSlide from "./slides/FileMoverSlide";
import CompletionSlide from "./slides/CompletionSlide";

import { Button } from "@/components/bakaui";
import Carousel from "@/components/bakaui/components/Carousel";
import Modal from "@/components/bakaui/components/Modal";

interface OnboardingModalProps {
  visible: boolean;
  onComplete: () => void;
}

const TOTAL_SLIDES = 8;

const OnboardingModal = ({ visible, onComplete }: OnboardingModalProps) => {
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
          <WelcomeSlide />
          <PathMarkSlide />
          <ResourceProfileSlide />
          <ResourceBrowsingSlide />
          <FileProcessorSlide />
          <DownloaderSlide />
          <FileMoverSlide />
          <CompletionSlide />
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
            {t("onboarding.skip")}
          </Button>
          <div className="flex gap-2">
            <Button isDisabled={isFirst} variant="flat" onPress={handlePrev}>
              {t("onboarding.previous")}
            </Button>
            <Button color="primary" onPress={handleNext}>
              {isLast ? t("onboarding.start") : t("onboarding.next")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default OnboardingModal;
