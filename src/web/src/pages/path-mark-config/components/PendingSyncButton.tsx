import { useState, useEffect, useCallback, useImperativeHandle, forwardRef, useRef } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineSync } from "react-icons/ai";

import PendingSyncListModal from "./PendingSyncListModal";
import { usePathMarkSyncCompletion } from "./pathMarkSyncTask";

import { Button, Badge } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { usePathMarksStore } from "@/stores/pathMarks";

export interface PendingSyncButtonRef {
  refresh: () => void;
}

interface PendingSyncButtonProps {
  buttonSize?: "sm" | "md";
  className?: string;
  onSyncComplete?: () => void;
}

const PendingSyncButton = forwardRef<PendingSyncButtonRef, PendingSyncButtonProps>(
  ({ buttonSize = "sm", className, onSyncComplete }, ref) => {
    const { t } = useTranslation();

    const [pendingSyncCount, setPendingSyncCount] = useState(0);
    const [showPendingSyncModal, setShowPendingSyncModal] = useState(false);

    const requestSequence = useRef(0);
    const deletedMarkIds = usePathMarksStore((state) => state.deletedMarkIds);

    // Load pending sync count
    const loadPendingSyncCount = useCallback(async () => {
      const request = ++requestSequence.current;

      try {
        const response = await BApi.pathMark.getPendingPathMarksCount();

        if (request === requestSequence.current && !response?.code) {
          setPendingSyncCount(response?.data ?? 0);
        }
      } catch (error) {
        console.error("Failed to load pending sync count", error);
      }
    }, []);

    useEffect(() => {
      loadPendingSyncCount();

      return () => {
        ++requestSequence.current;
      };
    }, [loadPendingSyncCount, deletedMarkIds]);

    usePathMarkSyncCompletion(() => {
      loadPendingSyncCount();
      onSyncComplete?.();
    });

    // Expose refresh method via ref
    useImperativeHandle(
      ref,
      () => ({
        refresh: loadPendingSyncCount,
      }),
      [loadPendingSyncCount],
    );

    const handleSyncComplete = useCallback(() => {
      loadPendingSyncCount();
      onSyncComplete?.();
    }, [loadPendingSyncCount, onSyncComplete]);

    return (
      <>
        <Badge
          color="warning"
          content={pendingSyncCount}
          isInvisible={pendingSyncCount === 0}
          size="sm"
        >
          <Button
            className={className}
            color={pendingSyncCount > 0 ? "warning" : "default"}
            size={buttonSize}
            startContent={<AiOutlineSync />}
            variant="flat"
            onPress={() => setShowPendingSyncModal(true)}
          >
            {t("pathMarkConfig.action.syncMarks")}
          </Button>
        </Badge>

        {showPendingSyncModal && (
          <PendingSyncListModal
            visible={showPendingSyncModal}
            onClose={() => setShowPendingSyncModal(false)}
            onSyncComplete={handleSyncComplete}
          />
        )}
      </>
    );
  },
);

PendingSyncButton.displayName = "PendingSyncButton";

export default PendingSyncButton;
