"use client";

import type { CarouselRef } from "antd/es/carousel";

import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineFilter,
  AiOutlineFunction,
  AiOutlineSetting,
  AiOutlineCheckCircle,
  AiOutlineNodeIndex,
} from "react-icons/ai";
import { MdBatchPrediction } from "react-icons/md";

import Modal from "@/components/bakaui/components/Modal";
import Carousel from "@/components/bakaui/components/Carousel";
import { Button } from "@/components/bakaui";

interface BulkModificationGuideModalProps {
  visible: boolean;
  onComplete: () => void;
}

const TOTAL_SLIDES = 5;

const BulkModificationGuideModal = ({ visible, onComplete }: BulkModificationGuideModalProps) => {
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
                <MdBatchPrediction className="text-4xl text-primary" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("bulkModificationGuide.welcome.title")}
              </h2>

              <p className="text-sm text-default-500 text-center max-w-lg">
                {t("bulkModificationGuide.welcome.description")}
              </p>

              <div className="px-3 py-2 rounded-lg bg-primary/5 border border-primary/20 max-w-lg w-full mt-4">
                <p className="text-default-700 text-sm">
                  {t("bulkModificationGuide.welcome.highlight")}
                </p>
              </div>
            </div>
          </div>

          {/* Slide 2: Workflow Overview */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-secondary/10">
                <AiOutlineNodeIndex className="text-4xl text-secondary" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("bulkModificationGuide.workflow.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("bulkModificationGuide.workflow.subtitle")}
              </p>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary font-bold text-sm">
                    1
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.workflow.step1Title")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.workflow.step1Desc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary font-bold text-sm">
                    2
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.workflow.step2Title")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.workflow.step2Desc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-warning/10 text-warning font-bold text-sm">
                    3
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.workflow.step3Title")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.workflow.step3Desc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    4
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.workflow.step4Title")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.workflow.step4Desc")}
                    </p>
                  </div>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 3: Value Sources */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-warning/10">
                <AiOutlineSetting className="text-4xl text-warning" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("bulkModificationGuide.valueSources.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("bulkModificationGuide.valueSources.subtitle")}
              </p>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-primary/5 border border-primary/20">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary">
                    <AiOutlineSetting className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-primary text-sm">
                      {t("bulkModificationGuide.valueSources.fixedValue")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.valueSources.fixedValueDesc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-secondary/10 text-secondary">
                    <AiOutlineFunction className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.valueSources.variable")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.valueSources.variableDesc")}
                    </p>
                  </div>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-danger/10 text-danger">
                    <AiOutlineFilter className="text-lg" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <span className="font-medium text-default-700 text-sm">
                      {t("bulkModificationGuide.valueSources.delete")}
                    </span>
                    <p className="text-sm text-default-500">
                      {t("bulkModificationGuide.valueSources.deleteDesc")}
                    </p>
                  </div>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 4: Pipeline Processing */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-success/10">
                <AiOutlineNodeIndex className="text-4xl text-success" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("bulkModificationGuide.pipeline.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("bulkModificationGuide.pipeline.subtitle")}
              </p>

              <div className="px-3 py-2 rounded-lg bg-warning/5 border border-warning/20 max-w-lg w-full mb-3">
                <p className="text-default-700 text-sm">
                  {t("bulkModificationGuide.pipeline.explanation")}
                </p>
              </div>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    1
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.pipeline.example1")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    2
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.pipeline.example2")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-success/10 text-success font-bold text-sm">
                    3
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.pipeline.example3")}
                  </span>
                </li>
              </ul>
            </div>
          </div>

          {/* Slide 5: Save & Reuse */}
          <div>
            <div className="flex flex-col items-center px-8 py-6">
              <div className="w-14 h-14 mb-4 flex items-center justify-center rounded-2xl bg-primary/10">
                <AiOutlineCheckCircle className="text-4xl text-primary" />
              </div>

              <h2 className="text-xl font-bold mb-2 text-center">
                {t("bulkModificationGuide.saveReuse.title")}
              </h2>

              <p className="text-sm text-default-500 text-center mb-4 max-w-lg">
                {t("bulkModificationGuide.saveReuse.subtitle")}
              </p>

              <ul className="space-y-2 max-w-lg w-full">
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary font-bold text-sm">
                    1
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.saveReuse.point1")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary font-bold text-sm">
                    2
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.saveReuse.point2")}
                  </span>
                </li>
                <li className="flex items-center gap-3 px-3 py-2 rounded-lg bg-default-100">
                  <div className="flex-shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-primary/10 text-primary font-bold text-sm">
                    3
                  </div>
                  <span className="text-default-700 text-sm">
                    {t("bulkModificationGuide.saveReuse.point3")}
                  </span>
                </li>
              </ul>

              <div className="px-3 py-2 rounded-lg bg-success/5 border border-success/20 max-w-lg w-full mt-3">
                <p className="text-default-700 text-sm">
                  {t("bulkModificationGuide.saveReuse.tip")}
                </p>
              </div>
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
            {t("bulkModificationGuide.skip")}
          </Button>
          <div className="flex gap-2">
            <Button isDisabled={isFirst} variant="flat" onPress={handlePrev}>
              {t("bulkModificationGuide.previous")}
            </Button>
            <Button color="primary" onPress={handleNext}>
              {isLast ? t("bulkModificationGuide.getStarted") : t("bulkModificationGuide.next")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

export default BulkModificationGuideModal;
