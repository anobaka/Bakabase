import BetaChip from "../BetaChip";

import { EnhancerIcon } from "@/components/Enhancer";
import { EnhancerId } from "@/sdk/constants";

type Props = {
  enhancer: { id: number; name: string };
};
const BriefEnhancer = ({ enhancer }: Props) => {
  return (
    <div className={"flex items-center gap-1"}>
      <EnhancerIcon id={enhancer.id} />
      {enhancer.name}
      {enhancer.id == EnhancerId.Kodi && <BetaChip />}
      {enhancer.id == EnhancerId.Tmdb && <BetaChip />}
      {enhancer.id == EnhancerId.Av && <BetaChip />}
    </div>
  );
};

BriefEnhancer.displayName = "BriefEnhancer";

export default BriefEnhancer;
