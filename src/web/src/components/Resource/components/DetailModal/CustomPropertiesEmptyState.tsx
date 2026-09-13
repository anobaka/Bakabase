"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineAppstore, AiOutlineArrowRight } from "react-icons/ai";
import { useNavigate } from "react-router-dom";

import { Button } from "@/components/bakaui";

const CustomPropertiesEmptyState = ({ onNavigate }: { onNavigate?: () => void }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <div className="flex items-start gap-3 rounded-xl border border-dashed border-default-200 bg-default-50/60 p-4">
      <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
        <AiOutlineAppstore aria-hidden className="text-xl" />
      </span>
      <div className="min-w-0 flex-1">
        <h3 className="text-sm font-medium text-default-700">
          {t<string>("resource.empty.noCustomPropertyBound")}
        </h3>
        <p className="mt-1 text-sm leading-relaxed text-default-500">
          {t<string>("resource.empty.customPropertiesDescription")}
        </p>
        <Button
          className="mt-3"
          color="primary"
          endContent={<AiOutlineArrowRight aria-hidden className="text-base" />}
          size="sm"
          variant="flat"
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
