"use client";

import { useTranslation } from "react-i18next";

import { Alert } from "@/components/bakaui";
import { DependentComponentStatus } from "@/sdk/constants";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import dependentComponentIds from "@/core/models/Constants/DependentComponentIds";

export default function LuxRequired() {
  const { t } = useTranslation();

  const dependentComponentContexts = useDependentComponentContextsStore(
    (state) => state.contexts,
  );

  const luxState = dependentComponentContexts?.find(
    (d) => d.id === dependentComponentIds.Lux,
  );

  const isLuxMissing = luxState?.status !== DependentComponentStatus.Installed;

  // If we only want to show when missing and it's not missing, don't render
  if (!isLuxMissing) {
    return null;
  }

  // Lux is missing, show error
  return (
    <Alert
      color="danger"
      title={t<string>(
        "This function is not working because Lux is not found, check it in system configurations",
      )}
    />
  );
}
