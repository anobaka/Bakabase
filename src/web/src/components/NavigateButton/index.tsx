"use client";

import type { ReactNode } from "react";

import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";

import { Button, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  to: string;
  children: ReactNode;
  confirmMessage?: string;
  size?: "sm" | "md" | "lg";
  color?: "default" | "primary" | "secondary" | "success" | "warning" | "danger";
  variant?: "solid" | "bordered" | "light" | "flat" | "faded" | "shadow" | "ghost";
  className?: string;
};

const NavigateButton = ({
  to,
  children,
  confirmMessage,
  size = "sm",
  color = "primary",
  variant = "light",
  className,
}: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();

  return (
    <Button
      className={className}
      color={color}
      size={size}
      variant={variant}
      onPress={() => {
        const modal = createPortal(Modal, {
          defaultVisible: true,
          title: t<string>("common.confirm.navigateAway"),
          children: confirmMessage ?? t<string>("common.confirm.navigateAwayMessage"),
          footer: {
            actions: ["cancel", "ok"],
          },
          onOk: () => {
            modal.destroy();
            navigate(to);
          },
        });
      }}
    >
      {children}
    </Button>
  );
};

NavigateButton.displayName = "NavigateButton";

export default NavigateButton;
