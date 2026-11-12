import { useEffect, useRef } from "react";
import {
  ExHentaiConfigField,
  ExHentaiConfigPanel,
} from "@/components/ThirdPartyConfig/platforms/ExHentaiConfig";
import { useExHentaiOptionsStore } from "@/stores/options";

export default function ExHentaiPreparation({ onChanged }: { onChanged?: () => void }) {
  const cookie = useExHentaiOptionsStore((state) => state.data.cookie);
  const previous = useRef(cookie);
  useEffect(() => {
    if (previous.current !== cookie) {
      previous.current = cookie;
      onChanged?.();
    }
  }, [cookie]);
  return <ExHentaiConfigPanel fields={[ExHentaiConfigField.Accounts]} />;
}
