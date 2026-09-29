import type { ReactNode } from "react";
import type { BakabaseServiceModelsViewRemoteAccessAddressViewModel as RemoteAccessAddress } from "@/sdk/Api";

import AddressList from "./AddressList";

/**
 * Everything the other device types, side by side: this device's address, and a code with
 * the button that makes one. Management hands out pairing codes, library sharing share
 * codes; the block is the same, only the words and the code differ.
 */
export default function InviteBlock({
  addresses,
  target,
  context,
  tip,
  code,
  status,
  action,
}: {
  addresses: readonly RemoteAccessAddress[] | undefined;
  target: string;
  context: "manage" | "sharing";
  /** What the code is for and how long it lasts. */
  tip: string;
  /** The digits, while they still work. */
  code?: string;
  /** How long the code lasts, that it expired, or that one is outstanding. */
  status?: ReactNode;
  /** The button that makes a code. */
  action: ReactNode;
}) {
  return (
    <div className="grid gap-3 md:grid-cols-2">
      <div className="rounded-lg border border-default-200 p-3">
        <AddressList addresses={addresses} context={context} target={target} variant="compact" />
      </div>
      <div className="space-y-2 rounded-lg border border-default-200 p-3">
        <p className="text-xs text-default-500">{tip}</p>
        {code && <code className="block text-2xl tracking-[0.25em]">{code}</code>}
        {status}
        {action}
      </div>
    </div>
  );
}
