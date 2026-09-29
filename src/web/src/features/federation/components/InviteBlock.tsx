import type { ReactNode } from "react";
import type { BakabaseServiceModelsViewRemoteAccessAddressViewModel as RemoteAccessAddress } from "@/sdk/Api";

import AddressList from "./AddressList";

/**
 * Everything the other device types, side by side: this device's address, and a code with
 * the button that makes one. Management hands out pairing codes, library sharing share
 * codes; the block is the same, only the words and the code differ.
 *
 * Laid out against the page's container (`@3xl`), not the window: beside the app's sidebar a
 * window wide enough for two columns can leave the content too narrow for them.
 */
export default function InviteBlock({
  addresses,
  addressesError,
  onRetryAddresses,
  target,
  context,
  tip,
  code,
  status,
  action,
}: {
  /** Undefined until remote access's settings are read; see `addressesError`. */
  addresses: readonly RemoteAccessAddress[] | undefined;
  /** Why remote access's settings could not be read, with a way to try again. */
  addressesError?: Error;
  onRetryAddresses?: () => void;
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
    <div className="grid gap-3 @3xl:grid-cols-2">
      <div className="rounded-lg border border-default-200 p-3">
        <AddressList
          addresses={addresses}
          context={context}
          error={addressesError}
          target={target}
          variant="compact"
          onRetry={onRetryAddresses}
        />
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
