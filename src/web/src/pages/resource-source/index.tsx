"use client";

import { useTranslation } from "react-i18next";
import { Card, CardBody, Button, Chip } from "@heroui/react";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineDesktop,
  AiOutlineCloud,
  AiOutlineShop,
  AiOutlinePicture,
} from "react-icons/ai";

import { ResourceSource, ResourceSourceLabel } from "@/sdk/constants";

const sourceConfig = [
  {
    source: ResourceSource.FileSystem,
    icon: AiOutlineDesktop,
    color: "default" as const,
    path: "/resource",
    configPath: "/media-library",
  },
  {
    source: ResourceSource.Steam,
    icon: AiOutlineCloud,
    color: "primary" as const,
    path: "/steam-apps",
    configPath: "/third-party-configuration",
  },
  {
    source: ResourceSource.DLsite,
    icon: AiOutlineShop,
    color: "secondary" as const,
    path: "/dlsite-works",
    configPath: "/third-party-configuration",
  },
  {
    source: ResourceSource.ExHentai,
    icon: AiOutlinePicture,
    color: "warning" as const,
    path: "/exhentai-galleries",
    configPath: "/third-party-configuration",
  },
];

export default function ResourceSourcePage() {
  const { t } = useTranslation();
  const navigate = useNavigate();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold">{t("resourceSource.title")}</h1>
        <p className="text-default-500 mt-1">
          {t("resourceSource.description")}
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {sourceConfig.map((cfg) => {
          const Icon = cfg.icon;
          const label =
            ResourceSourceLabel[cfg.source] || String(cfg.source);

          return (
            <Card key={cfg.source} className="border-small border-default-200">
              <CardBody className="flex flex-row items-center gap-4 p-4">
                <div className="shrink-0">
                  <div className="w-12 h-12 rounded-lg bg-default-100 flex items-center justify-center">
                    <Icon className="text-2xl" />
                  </div>
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-lg">{label}</span>
                    <Chip size="sm" color={cfg.color} variant="flat">
                      {t(`enum.resourceSource.${label.charAt(0).toLowerCase() + label.slice(1).replace(/\s+/g, "")}`)}
                    </Chip>
                  </div>
                  <p className="text-sm text-default-500 mt-1">
                    {t(`resourceSource.${label.charAt(0).toLowerCase() + label.slice(1).replace(/\s+/g, "")}.description`, { defaultValue: "" })}
                  </p>
                </div>
                <div className="shrink-0 flex gap-2">
                  <Button
                    size="sm"
                    variant="flat"
                    onPress={() => navigate(cfg.configPath)}
                  >
                    {t("resourceSource.action.configure")}
                  </Button>
                  <Button
                    size="sm"
                    color="primary"
                    onPress={() => navigate(cfg.path)}
                  >
                    {t("resourceSource.action.viewAll")}
                  </Button>
                </div>
              </CardBody>
            </Card>
          );
        })}
      </div>
    </div>
  );
}
