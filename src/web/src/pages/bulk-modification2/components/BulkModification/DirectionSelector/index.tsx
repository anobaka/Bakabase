"use client";

import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

type Subject = "operation" | "positioning";

type Props = {
  isReversed?: boolean;
  onChange?: (isReversed: boolean) => void;
  subject: Subject;
};

const I18NKeyMap: Record<Subject, { forward: string; backward: string }> = {
  operation: {
    backward: "Backward",
    forward: "Forward",
  },
  positioning: {
    backward: "Position.End",
    forward: "Position.Beginning",
  },
};

export default ({ isReversed, onChange, subject }: Props) => {
  const { t } = useTranslation();

  return (
    <Button
      color={"secondary"}
      variant={"light"}
      onClick={() => {
        onChange?.(!isReversed);
      }}
    >
      {t<string>(
        isReversed
          ? I18NKeyMap[subject]["backward"]
          : I18NKeyMap[subject]["forward"],
      )}
    </Button>
  );
};
