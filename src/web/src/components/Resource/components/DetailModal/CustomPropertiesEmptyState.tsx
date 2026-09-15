"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineAppstore, AiOutlineArrowRight } from "react-icons/ai";
import { useNavigate } from "react-router-dom";

import { Button } from "@/components/bakaui";

const CustomPropertiesEmptyState = ({ onNavigate }: { onNavigate?: () => void }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <div className="flex flex-wrap items-start gap-3 rounded-xl bg-default-50/60 p-3">
      <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-default-100 text-default-500">
        <AiOutlineAppstore aria-hidden className="text-lg" />
      </span>
      <div className="min-w-0 flex-1">
        <h3 className="text-sm font-medium text-default-700">
          {t<string>("resource.empty.noCustomPropertyBound")}
        </h3>
        <p className="mt-1 text-xs leading-relaxed text-default-500">
          {t<string>("resource.empty.customPropertiesDescription")}
        </p>
        <Button
          className="mt-2 h-auto min-w-0 justify-start px-0 py-1"
          color="primary"
          endContent={<AiOutlineArrowRight aria-hidden className="text-base" />}
          size="sm"
          variant="light"
          onPress={() => {
            onNavigate?.();
            navigate("/resource-profile");
          }}
        >
          {t<string>("resource.empty.configureResourceProfile")}
        </Button>
      </div>
    </div>
  );
};

export default CustomPropertiesEmptyState;
