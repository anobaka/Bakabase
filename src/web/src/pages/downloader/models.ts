import { ThirdPartyId } from "@/sdk/constants";

/**
 * List of third party IDs that are currently under development
 * These will be marked with a developing chip in the UI
 */
export const developingThirdPartyIds: ThirdPartyId[] = [
  ThirdPartyId.Fantia,
  ThirdPartyId.Fanbox,
  ThirdPartyId.Cien,
  ThirdPartyId.Patreon,
];

/**
 * Check if a third party ID is under development
 * @param thirdPartyId - The third party ID to check
 * @returns true if the third party is under development
 */
export const isThirdPartyDeveloping = (thirdPartyId: ThirdPartyId): boolean => {
  return developingThirdPartyIds.includes(thirdPartyId);
};
