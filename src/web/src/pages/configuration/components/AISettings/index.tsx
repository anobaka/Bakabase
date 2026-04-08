"use client";

import { useTranslation } from "react-i18next";
import {
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@heroui/react";

import { AiFeature } from "@/sdk/constants";
import AiProviderPanel from "@/components/AiProviderPanel";
import AiFeaturePanel from "@/components/AiFeaturePanel";
import QuotaSettings from "./QuotaSettings";
import CacheSettings from "./CacheSettings";

const aiSettingsFeatures = [
  AiFeature.Default,
  AiFeature.Enhancer,
  AiFeature.Translation,
  AiFeature.FileProcessor,
];

const AISettings = () => {
  const { t } = useTranslation();

  return (
    <div className="group">
      <div className="settings">
        <AiProviderPanel />

        <div className="mt-4">
          <AiFeaturePanel features={aiSettingsFeatures} />
        </div>

        <Table removeWrapper className="mt-4">
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.cache.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell>
                <span className="text-sm">{t("configuration.ai.cache.responseCache")}</span>
              </TableCell>
              <TableCell><CacheSettings /></TableCell>
            </TableRow>
          </TableBody>
        </Table>

        <Table removeWrapper className="mt-4">
          <TableHeader>
            <TableColumn width={200}>
              {t<string>("configuration.ai.quota.title")}
            </TableColumn>
            <TableColumn>&nbsp;</TableColumn>
          </TableHeader>
          <TableBody>
            <TableRow>
              <TableCell>
                <span className="text-sm">{t("configuration.ai.quota.auditContent")}</span>
              </TableCell>
              <TableCell><QuotaSettings /></TableCell>
            </TableRow>
          </TableBody>
        </Table>

      </div>
    </div>
  );
};

AISettings.displayName = "AISettings";

export default AISettings;
