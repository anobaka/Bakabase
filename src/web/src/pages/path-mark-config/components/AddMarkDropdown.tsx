"use client";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineAim, AiOutlinePlus } from "react-icons/ai";

import {
  Button,
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
} from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import {
  ResourceDescription,
  PropertyDescription,
  MediaLibraryDescription,
} from "@/components/Chips/Terms";

type Props = {
  onSelect: (markType: PathMarkType) => void;
  compact?: boolean;
};

const AddMarkDropdown = ({ onSelect, compact }: Props) => {
  const { t } = useTranslation();

  return (
    <Dropdown>
      <DropdownTrigger>
        {compact ? (
          <Button
            isIconOnly
            className="min-w-0 w-5 h-5"
            color="success"
            size="sm"
            variant="light"
          >
            <AiOutlinePlus className="text-base" />
          </Button>
        ) : (
          <Button
            className="min-w-0 px-2 text-default-400 hover:text-primary transition-colors"
            size="sm"
            startContent={<AiOutlineAim className="text-lg" />}
            variant="light"
          >
            {t("pathMarkConfig.action.addMark")}
          </Button>
        )}
      </DropdownTrigger>
      <DropdownMenu
        aria-label="Mark type selection"
        className="max-w-[600px]"
        onAction={(key) => onSelect(Number(key) as PathMarkType)}
      >
        <DropdownItem
          key={PathMarkType.Resource}
          className="text-success"
          description={<ResourceDescription />}
        >
          {t("common.label.resource")}
        </DropdownItem>
        <DropdownItem
          key={PathMarkType.Property}
          className="text-primary"
          description={<PropertyDescription />}
        >
          {t("common.label.property")}
        </DropdownItem>
        <DropdownItem
          key={PathMarkType.MediaLibrary}
          className="text-secondary"
          description={<MediaLibraryDescription />}
        >
          {t("common.label.mediaLibrary")}
        </DropdownItem>
      </DropdownMenu>
    </Dropdown>
  );
};

export default AddMarkDropdown;
