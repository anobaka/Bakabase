import BetaChip from "../BetaChip";

import { EnhancerIcon } from "@/components/Enhancer";
import { EnhancerId } from "@/sdk/constants";

type Props = {
  enhancer: { id: number; name: string };
};

export default ({ enhancer }: Props) => {
  return (
    <div className={"flex items-center gap-1"}>
      <EnhancerIcon id={enhancer.id} />
      {enhancer.name}
      {enhancer.id == EnhancerId.Kodi && <BetaChip />}
    </div>
  );
};
