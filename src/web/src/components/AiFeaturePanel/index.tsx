"use client";

import type { FC } from "react";

import { useTranslation } from "react-i18next";
import {
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@heroui/react";

import { AiFeature, AiFeatureLabel } from "@/sdk/constants";
import AiFeatureConfigShortcut from "@/components/AiFeatureConfigShortcut";

export interface AiFeaturePanelProps {
  /** Which features to show. Defaults to all. */
  features?: AiFeature[];
}

const defaultFeatures = [
  AiFeature.Default,
  AiFeature.Enhancer,
  AiFeature.Translation,
  AiFeature.FileProcessor,
  AiFeature.PostParser,
];

const AiFeaturePanel: FC<AiFeaturePanelProps> = ({ features = defaultFeatures }) => {
  const { t } = useTranslation();

  return (
    <Table removeWrapper>
      <TableHeader>
        <TableColumn width={200}>
          {t<string>("configuration.ai.scenarios")}
        </TableColumn>
        <TableColumn>&nbsp;</TableColumn>
      </TableHeader>
      <TableBody>
        {features.map((feature) => (
          <TableRow key={`feature-${feature}`} className="hover:bg-[var(--bakaui-overlap-background)]">
            <TableCell>
              <div className="flex items-center">
                {t<string>(`configuration.ai.feature.${AiFeatureLabel[feature]}`)}
              </div>
            </TableCell>
            <TableCell>
              <AiFeatureConfigShortcut feature={feature} label={false} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
};

AiFeaturePanel.displayName = "AiFeaturePanel";

export default AiFeaturePanel;
